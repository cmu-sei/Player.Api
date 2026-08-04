// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// Covers what <c>Export</c> does when a view's file rows point at bytes it cannot read — the branch
/// that packs <c>errors.txt</c> and flags the result, and what the resulting archive is worth to an
/// import.
/// </summary>
/// <remarks>
/// A file row whose <c>Path</c> names nothing on disk is the whole setup, but the paths sit inside a
/// directory that does exist: a missing parent reports as <see cref="DirectoryNotFoundException"/> on both
/// platforms and makes <c>Delete</c>'s per-file cleanup throw.
/// </remarks>
public class ViewExportFileErrorTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    private readonly string _fileRoot =
        Path.Combine(Path.GetTempPath(), $"player-export-errors-{Guid.NewGuid():N}");

    /// <summary>
    /// An unreadable file does not fail the export — the reason is packed as a text file instead, and
    /// <c>HasErrors</c> is what the endpoint turns into the archive-errors header.
    /// </summary>
    [Fact]
    public async Task Export_records_a_file_whose_bytes_are_missing_as_an_error()
    {
        var view = TestData.View("Missing bytes");
        await Seed(view);
        var file = await SeedMissingFile(view, "notes.txt");

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        Assert.True(archive.HasErrors);

        var errors = Errors(archive);
        Assert.Contains($"View: {view.Id}", errors);
        Assert.Contains($"File: notes.txt ({file.Id})", errors);
        Assert.Contains($"Error: {typeof(FileNotFoundException)}", errors);
    }

    /// <summary>
    /// The file stays listed in views.json with no bytes packed beside it, which is what makes the
    /// archive self-inconsistent rather than merely incomplete — see
    /// <see cref="An_export_with_a_missing_file_produces_an_archive_that_imports_as_a_failure"/> for the
    /// consequence.
    /// </summary>
    [Fact]
    public async Task Export_still_lists_a_file_whose_bytes_are_missing_in_the_view_json()
    {
        var view = TestData.View();
        await Seed(view);
        var file = await SeedMissingFile(view, "notes.txt");

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        var exported = Assert.Single(ArchiveHelper.ReadExportedViews(archive, Archives));
        Assert.Equal(file.Id, Assert.Single(exported.Files).id);

        Assert.Equal(
            new[] { ViewConstants.ExportFileName, ViewConstants.ErrorFileName }.Order(),
            Files(archive).Keys.Order());
    }

    /// <summary>
    /// One unreadable file among several is reported without costing the others their bytes, so an
    /// operator loses only the file that failed.
    /// </summary>
    [Fact]
    public async Task Export_packs_the_readable_file_and_reports_only_the_unreadable_one()
    {
        var view = TestData.View();
        await Seed(view);
        var readable = await SeedReadableFile(view, "readable.txt", "packed bytes");
        var missing = await SeedMissingFile(view, "missing.txt");

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });
        var files = Files(archive);

        // Sorted, because the file rows come from a query with no ORDER BY.
        Assert.Equal(
            new[]
            {
                $"{readable.Id}-readable.txt",
                ViewConstants.ExportFileName,
                ViewConstants.ErrorFileName
            }.Order(),
            files.Keys.Order());

        Assert.Equal("packed bytes", Encoding.UTF8.GetString(files[$"{readable.Id}-readable.txt"]));

        var errors = Errors(archive);
        Assert.Contains($"File: missing.txt ({missing.Id})", errors);
        Assert.DoesNotContain(readable.Id.ToString(), errors);
    }

    /// <summary>
    /// The control for the three above: errors.txt appears only when a read failed.
    /// <c>ViewRequestTests.Export_produces_an_archive_containing_the_view_json</c> already pins
    /// <c>HasErrors</c> false for a view with no files at all.
    /// </summary>
    [Fact]
    public async Task Export_of_a_readable_file_packs_it_with_no_error_file()
    {
        var view = TestData.View();
        await Seed(view);
        var file = await SeedReadableFile(view, "notes.txt", "readable bytes");

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        Assert.False(archive.HasErrors);
        Assert.Equal(
            new[] { $"{file.Id}-notes.txt", ViewConstants.ExportFileName }.Order(),
            Files(archive).Keys.Order());
    }

    /// <summary>
    /// Characterizes issue 43: views.json names a file whose bytes were never packed, so
    /// <c>ViewImporter.ValidateFiles</c> reports a per-file failure — and any failure rejects the whole
    /// view, making a partial export a total loss on import.
    /// </summary>
    [Fact]
    public async Task An_export_with_a_missing_file_produces_an_archive_that_imports_as_a_failure()
    {
        var view = TestData.View("Unimportable");
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);
        var file = await SeedMissingFile(view, "notes.txt");

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        // The exported view keeps its id, so it has to be gone before the import will accept it.
        await SendAsync(new Delete.Command { Id = view.Id });

        var result = await SendAsync(new Import.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive)
        });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(view.Id, failure.Id);
        Assert.Equal($"File notes.txt ({file.Id}): No matching file found in archive", failure.Reason);

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
    }

    /// <summary>
    /// Bounds issue 43: the blast radius is the one view that owns the unreadable file. Its neighbour in the
    /// same archive imports normally, and errors.txt attributes the failure to the right view.
    /// </summary>
    [Fact]
    public async Task An_unreadable_file_costs_only_the_view_that_owns_it()
    {
        var broken = TestData.View("Broken");
        var intact = TestData.View("Intact");
        await Seed(broken, TestData.Team(broken.Id, "Alpha"), intact, TestData.Team(intact.Id, "Beta"));
        await SeedMissingFile(broken, "notes.txt");

        var archive = await SendAsync(new Export.Query { Ids = [], ArchiveType = ArchiveType.zip });

        Assert.Contains($"View: {broken.Id}", Errors(archive));
        Assert.DoesNotContain($"View: {intact.Id}", Errors(archive));

        await SendAsync(new Delete.Command { Id = broken.Id });
        await SendAsync(new Delete.Command { Id = intact.Id });

        var result = await SendAsync(new Import.Command { Archive = ArchiveHelper.AsFormFile(archive) });

        Assert.Equal(broken.Id, Assert.Single(result.Failures).Id);

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == broken.Id, Ct));
        Assert.True(await db.Views.AnyAsync(x => x.Id == intact.Id, Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private IArchiveService Archives => RootHost.Resolve<IArchiveService>();

    private Dictionary<string, byte[]> Files(ArchiveResult archive) =>
        ArchiveHelper.ExtractFiles(archive, Archives);

    private string Errors(ArchiveResult archive) =>
        Encoding.UTF8.GetString(Files(archive)[ViewConstants.ErrorFileName]);

    /// <summary>A file row whose path names nothing, in a directory that exists.</summary>
    private Task<FileEntity> SeedMissingFile(ViewEntity view, string name) =>
        SeedFile(view, name, NewPath(name));

    private async Task<FileEntity> SeedReadableFile(ViewEntity view, string name, string content)
    {
        var path = NewPath(name);
        await File.WriteAllTextAsync(path, content, Ct);
        return await SeedFile(view, name, path);
    }

    /// <summary>Unique per file, so nothing a test writes is shared with another file row.</summary>
    private string NewPath(string name)
    {
        Directory.CreateDirectory(_fileRoot);
        return Path.Combine(_fileRoot, $"{Guid.NewGuid():N}-{name}");
    }

    private async Task<FileEntity> SeedFile(ViewEntity view, string name, string path)
    {
        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            TeamIds = [],
            View = view
        };

        await Seed(file);
        return file;
    }

    public override async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_fileRoot))
        {
            Directory.Delete(_fileRoot, recursive: true);
        }

        await base.DisposeAsync();
    }
}
