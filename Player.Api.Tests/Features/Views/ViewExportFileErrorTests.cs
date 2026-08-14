// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Constants;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// Covers what the export route does when a view's file rows point at bytes it cannot read — the branch
/// that packs <c>errors.txt</c> and marks the response, and what the resulting archive is worth to an
/// import.
/// </summary>
/// <remarks>
/// <para>
/// A file row whose <c>Path</c> names nothing on disk is the whole setup, but the paths sit inside a
/// directory that does exist: a missing parent reports as <see cref="DirectoryNotFoundException"/> on both
/// platforms and makes the delete route's per-file cleanup throw.
/// </para>
/// <para>
/// The rows and the bytes are arranged directly rather than over HTTP, because an upload can only produce
/// a file the application can read. What goes through the application is the read: the handler opens
/// whatever absolute path the row carries, so a directory of this test's own is where it looks — the
/// harness's <c>FileUpload:basePath</c> decides where new uploads land, not where an existing row points.
/// </para>
/// </remarks>
public class ViewExportFileErrorTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>
    /// The import route with both of its required flags, which bind as non-nullable value types — a
    /// request omitting either is answered with a bare 400 before the handler is reached.
    /// </summary>
    private const string ImportRoute =
        "api/views/actions/import?matchRolesByName=true&matchApplicationTemplatesByName=true";

    private readonly string _fileRoot =
        Path.Combine(Path.GetTempPath(), $"player-export-errors-{Guid.NewGuid():N}");

    // ---- Export ---------------------------------------------------------------------------------

    /// <summary>
    /// An unreadable file does not fail the export — the reason is packed as a text file instead, and the
    /// response carries the archive-errors header that tells a client the archive is short.
    /// </summary>
    [Fact]
    public async Task Export_records_a_file_whose_bytes_are_missing_as_an_error()
    {
        var view = TestData.View("Missing bytes");
        await Seed(view);
        var file = await SeedMissingFile(view, "notes.txt");

        var response = await Export(view.Id);

        Assert.True(HasArchiveErrors(response));

        var errors = await Errors(response);
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

        var response = await Export(view.Id);

        var exported = Assert.Single(await ExportedViews(response));
        Assert.Equal(file.Id, Assert.Single(exported.Files).id);

        Assert.Equal(
            new[] { ViewConstants.ExportFileName, ViewConstants.ErrorFileName }.Order(),
            (await Files(response)).Keys.Order());
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

        var response = await Export(view.Id);
        var files = await Files(response);

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

        var errors = await Errors(response);
        Assert.Contains($"File: missing.txt ({missing.Id})", errors);
        Assert.DoesNotContain(readable.Id.ToString(), errors);
    }

    /// <summary>
    /// The control for the three above: errors.txt is packed and the header set only when a read failed.
    /// <c>ViewRequestTests.Export_produces_an_archive_containing_the_view_json</c> pins the same for a
    /// view with no files at all.
    /// </summary>
    [Fact]
    public async Task Export_of_a_readable_file_packs_it_with_no_error_file()
    {
        var view = TestData.View();
        await Seed(view);
        var file = await SeedReadableFile(view, "notes.txt", "readable bytes");

        var response = await Export(view.Id);

        Assert.False(HasArchiveErrors(response));
        Assert.Equal(
            new[] { $"{file.Id}-notes.txt", ViewConstants.ExportFileName }.Order(),
            (await Files(response)).Keys.Order());
    }

    // ---- Import ---------------------------------------------------------------------------------

    /// <summary>
    /// Characterizes issue 43: views.json names a file whose bytes were never packed, so the importer
    /// rejects the whole view over it — a partial export is a total loss on import.
    /// </summary>
    [Fact]
    public async Task An_export_with_a_missing_file_produces_an_archive_that_imports_as_a_failure()
    {
        var view = TestData.View("Unimportable");
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);
        var file = await SeedMissingFile(view, "notes.txt");

        var exported = await Export(view.Id);

        // The exported view keeps its id, so it has to be gone before the import will accept it.
        await Delete(view.Id);

        var result = await Import(exported);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(view.Id, failure.Id);
        Assert.Equal($"File notes.txt ({file.Id}): No matching file found in archive", failure.Reason);

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
    }

    /// <summary>
    /// Bounds issue 43: the blast radius is the one view that owns the unreadable file. Its neighbour in
    /// the same archive imports normally, and errors.txt attributes the failure to the right view.
    /// </summary>
    [Fact]
    public async Task An_unreadable_file_costs_only_the_view_that_owns_it()
    {
        var broken = TestData.View("Broken");
        var intact = TestData.View("Intact");
        await Seed(broken, TestData.Team(broken.Id, "Alpha"), intact, TestData.Team(intact.Id, "Beta"));
        await SeedMissingFile(broken, "notes.txt");

        var exported = await Export();

        var errors = await Errors(exported);
        Assert.Contains($"View: {broken.Id}", errors);
        Assert.DoesNotContain($"View: {intact.Id}", errors);

        await Delete(broken.Id);
        await Delete(intact.Id);

        var result = await Import(exported);

        Assert.Equal(broken.Id, Assert.Single(result.Failures).Id);

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == broken.Id, Ct));
        Assert.True(await db.Views.AnyAsync(x => x.Id == intact.Id, Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Exports <paramref name="ids"/> as a zip, or every view when none are given. The archive type is
    /// required, so it is always in the query string.
    /// </summary>
    private async Task<HttpResponseMessage> Export(params Guid[] ids)
    {
        var response = await RootClient.GetAsync(
            $"api/views/actions/export?{string.Concat(ids.Select(x => $"ids={x}&"))}" +
            $"archiveType={ArchiveType.zip}",
            Ct);

        await AssertStatus(HttpStatusCode.OK, response);

        return response;
    }

    /// <summary>Deletes a view, which is what frees its id for the import to take.</summary>
    private async Task Delete(Guid viewId) => await AssertStatus(
        HttpStatusCode.NoContent, await RootClient.DeleteAsync($"api/views/{viewId}", Ct));

    /// <summary>Uploads the archive an export answered with, under the file part the importer binds.</summary>
    private async Task<Import.ImportViewsResult> Import(HttpResponseMessage exported)
    {
        using var upload = ArchiveHelper.AsUpload(
            await exported.Content.ReadAsByteArrayAsync(Ct), ArchiveName(exported));

        return await ReadAsync<Import.ImportViewsResult>(
            await RootClient.PostAsync(ImportRoute, upload, Ct));
    }

    /// <summary>The entries of the archive the response carried, by name.</summary>
    private static async Task<Dictionary<string, byte[]>> Files(HttpResponseMessage response) =>
        ArchiveHelper.ExtractFiles(
            await response.Content.ReadAsByteArrayAsync(Ct), ArchiveName(response));

    private static async Task<string> Errors(HttpResponseMessage response) =>
        Encoding.UTF8.GetString((await Files(response))[ViewConstants.ErrorFileName]);

    private static async Task<ViewExport[]> ExportedViews(HttpResponseMessage response) =>
        ArchiveHelper.ReadExportedViews(
            await response.Content.ReadAsByteArrayAsync(Ct), ArchiveName(response));

    /// <summary>
    /// The archive's file name, which the importer and the extractor read the archive type from.
    /// </summary>
    private static string ArchiveName(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    /// <summary>
    /// Whether the response carries the archive-errors marker. Both collections are checked because one
    /// of the assertions on it is a negative one, and a header looked for in the wrong place is absent
    /// from it.
    /// </summary>
    private static bool HasArchiveErrors(HttpResponseMessage response) =>
        response.Headers.Contains(HttpConstants.ArchiveErrorsHeader) ||
        response.Content.Headers.Contains(HttpConstants.ArchiveErrorsHeader);

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
