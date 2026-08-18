// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Services;

/// <summary>
/// File upload, download and deletion. This is the one service that writes outside the database, so
/// each test runs against a real directory and asserts on the bytes as well as the rows — a pointer
/// without a file, or a file no row points at, is the failure mode.
/// </summary>
public class FileServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), $"player-file-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// The upload limit the boundary tests configure. Small enough that a case can allocate the byte over
    /// it, and named here so that those cases read as offsets from a limit rather than as magic sizes.
    /// </summary>
    private const int Limit = 64;

    // ---- Upload -----------------------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_writes_the_file_and_records_a_pointer_to_it()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var uploaded = await Service().UploadAsync(Form(view.Id, [team.Id], ("notes.txt", "hello")), Ct);

        var model = Assert.Single(uploaded);
        Assert.Equal("notes.txt", model.Name);
        Assert.Equal([team.Id], model.teamIds);

        using var db = NewContext();
        var stored = db.Files.Include(x => x.View).Single();
        Assert.Equal(view.Id, stored.View.Id);
        Assert.Equal("hello", await File.ReadAllTextAsync(stored.Path, Ct));
    }

    /// <summary>
    /// Stored under a generated name in a per-view folder, so two views can hold files of the same name
    /// and one upload cannot overwrite another.
    /// </summary>
    [Fact]
    public async Task UploadAsync_stores_the_file_under_a_generated_name()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var service = Service();
        await service.UploadAsync(Form(view.Id, [team.Id], ("notes.txt", "one")), Ct);
        await service.UploadAsync(Form(view.Id, [team.Id], ("notes.txt", "two")), Ct);

        using var db = NewContext();
        var paths = db.Files.Select(x => x.Path).ToList();
        Assert.Equal(2, paths.Distinct().Count());
        Assert.All(paths, path =>
        {
            Assert.Equal(Path.Combine(_basePath, view.Id.ToString()), Path.GetDirectoryName(path));
            Assert.NotEqual("notes.txt", Path.GetFileName(path));
        });
    }

    /// <summary>
    /// The generated name carries two dots before its extension: <c>GetNameToStore</c>
    /// (<c>FileService.cs:352-358</c>) appends a separator to <c>Path.GetExtension</c>, which already
    /// returns one. Harmless on disk, and pinned because the name is what an operator sees there.
    /// </summary>
    /// <remarks>Turns red when the extension is appended without the extra separator.</remarks>
    [Fact]
    public async Task UploadAsync_stores_the_file_with_two_dots_before_its_extension()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Service().UploadAsync(Form(view.Id, [team.Id], ("notes.txt", "one")), Ct);

        using var db = NewContext();
        var stored = Path.GetFileName(db.Files.Single().Path);

        Assert.EndsWith("..txt", stored);
        Assert.True(Guid.TryParse(stored[..^5], out _), $"'{stored}' does not start with a guid");
    }

    [Fact]
    public async Task UploadAsync_accepts_several_files_in_one_request()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var uploaded = await Service().UploadAsync(
            Form(view.Id, [team.Id], ("first.txt", "1"), ("second.txt", "2")),
            Ct);

        Assert.Equal(["first.txt", "second.txt"], uploaded.Select(x => x.Name));
        // The rows as well as the return value, since the two are produced separately.
        Assert.Equal(["first.txt", "second.txt"], (await Stored()).Select(x => x.Name).Order());
    }

    /// <summary>
    /// A file is reachable only through a team, so an upload naming none has nothing that could ever read
    /// it back.
    /// </summary>
    [Fact]
    public async Task UploadAsync_refuses_a_form_with_no_teams()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().UploadAsync(Form(view.Id, null, ("notes.txt", "hello")), Ct));
    }

    /// <summary>
    /// The permission check that follows is made against the form's view alone, so every named team has to
    /// belong to it.
    /// </summary>
    [Fact]
    public async Task UploadAsync_refuses_a_team_from_another_view()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, other, otherTeam);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().UploadAsync(Form(view.Id, [otherTeam.Id], ("notes.txt", "hello")), Ct));
    }

    [Fact]
    public async Task UploadAsync_is_forbidden_for_a_caller_without_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous())
                .UploadAsync(Form(view.Id, [team.Id], ("notes.txt", "hello")), Ct));
    }

    [Fact]
    public async Task UploadAsync_refuses_a_disallowed_extension()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().UploadAsync(Form(view.Id, [team.Id], ("payload.exe", "hello")), Ct));
    }

    /// <summary>
    /// Characterizes issue 61. The allow-list is matched with a case-sensitive
    /// <see cref="string.EndsWith(string)"/> (<c>FileService.cs:302</c>), so the file refused here uploads
    /// fine once its extension is lower-cased. Cameras, scanners and Windows clients all produce
    /// upper-case extensions, and the caller is told only that the extension is invalid.
    /// </summary>
    /// <remarks>Turns red when the comparison becomes case-insensitive.</remarks>
    [Theory]
    [InlineData("NOTES.TXT")]
    [InlineData("notes.Txt")]
    public async Task UploadAsync_refuses_an_allowed_extension_in_the_wrong_case(string name)
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().UploadAsync(Form(view.Id, [team.Id], (name, "hello")), Ct));
    }

    /// <summary>
    /// The allow-list is a suffix match on the name rather than a comparison against
    /// <see cref="Path.GetExtension(string)"/>, but for a double extension the two agree: the last one is
    /// what decides, so <c>notes.exe.txt</c> is a text file the way a file system reads it.
    /// </summary>
    [Fact]
    public async Task UploadAsync_decides_a_double_extension_on_the_last_one()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var uploaded = await Service().UploadAsync(
            Form(view.Id, [team.Id], ("notes.exe.txt", "hello")),
            Ct);

        Assert.Equal("notes.exe.txt", Assert.Single(uploaded).Name);
        Assert.EndsWith("..txt", Assert.Single(await Stored()).Path);
    }

    /// <summary>
    /// An allowed extension anywhere but the end does not let the file through, which is the case the
    /// allow-list exists for.
    /// </summary>
    [Fact]
    public async Task UploadAsync_refuses_an_allowed_extension_that_is_not_the_last_one()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().UploadAsync(Form(view.Id, [team.Id], ("notes.txt.exe", "hello")), Ct));
    }

    /// <summary>
    /// A name with no extension has nothing for the suffix match to match, so it is refused rather than
    /// treated as a type nobody named.
    /// </summary>
    [Fact]
    public async Task UploadAsync_refuses_a_name_with_no_extension()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().UploadAsync(Form(view.Id, [team.Id], ("readme", "hello")), Ct));
    }

    /// <summary>
    /// Characterizes issue 61. <c>ValidateFileExtension</c> compares with
    /// <see cref="string.EndsWith(string)"/>, whose default is culture-sensitive, so characters the
    /// collation gives no weight are not read at all: this name matches <c>.txt</c> without ending in it
    /// under any ordinal reading. A soft hyphen is not an invalid file-name character, so it survives
    /// <c>SanitizeFileName</c> and the file lands on disk under an extension the allow-list does not
    /// contain. Nothing dangerous gets through this way — the tail still has to spell an allowed
    /// extension — but the check does not mean what it reads as, and the guidance for a non-linguistic
    /// comparison like this one is <see cref="StringComparison.Ordinal"/>.
    /// </summary>
    /// <remarks>Turns red when the comparison becomes ordinal.</remarks>
    [Fact]
    public async Task UploadAsync_accepts_an_extension_that_matches_only_under_the_current_culture()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        // notes.t<soft hyphen>xt
        const string name = "notes.t­xt";

        var uploaded = await Service().UploadAsync(Form(view.Id, [team.Id], (name, "hello")), Ct);

        Assert.Equal(name, Assert.Single(uploaded).Name);
        Assert.EndsWith("..t­xt", Assert.Single(await Stored()).Path);
    }

    /// <summary>
    /// The name is stored and later handed back as a download filename, so the characters a path cannot
    /// carry are dropped rather than rejected.
    /// </summary>
    [Fact]
    public async Task UploadAsync_strips_invalid_filename_characters()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var uploaded = await Service().UploadAsync(
            Form(view.Id, [team.Id], ($"..{Path.DirectorySeparatorChar}escaped.txt", "hello")),
            Ct);

        Assert.Equal("..escaped.txt", Assert.Single(uploaded).Name);
    }

    /// <summary>
    /// The limit is <c>stream.Length > maxSize</c> (<c>FileService.cs:323</c>), so a file of exactly the
    /// limit is accepted and the first refused byte is the one past it. An empty file is not a special
    /// case either: nothing looks for one, so it is written and pointed at like any other.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(Limit - 1)]
    [InlineData(Limit)]
    public async Task UploadAsync_accepts_a_file_up_to_the_limit(int size)
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Service(maxSize: Limit).UploadAsync(SizedForm(view.Id, team.Id, size), Ct);

        // The bytes rather than the row, since the limit is applied to the stream that produced them.
        Assert.Equal(size, new FileInfo(Assert.Single(await Stored()).Path).Length);
    }

    /// <summary>
    /// The size is checked before anything is created, so a refused upload leaves neither a row nor a
    /// directory for the view behind it.
    /// </summary>
    [Theory]
    [InlineData(Limit + 1)]
    [InlineData(Limit * 32)]
    public async Task UploadAsync_refuses_a_file_over_the_limit(int size)
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(maxSize: Limit).UploadAsync(SizedForm(view.Id, team.Id, size), Ct));

        Assert.Empty(await Stored());
        Assert.False(Directory.Exists(Path.Combine(_basePath, view.Id.ToString())));
    }

    /// <summary>
    /// Characterizes current behaviour. An unknown view is read as null and then dereferenced, so the
    /// caller gets a 500 rather than a 404. Flip to
    /// <see cref="EntityNotFoundException{ViewEntity}"/> once the null is handled.
    /// </summary>
    [Fact]
    public async Task UploadAsync_throws_for_an_unknown_view()
    {
        await Assert.ThrowsAsync<NullReferenceException>(
            () => Service().UploadAsync(Form(Guid.NewGuid(), [Guid.NewGuid()], ("notes.txt", "hello")), Ct));
    }

    // ---- Reads ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_returns_every_file()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        await SeedFile(view, "one.txt", teamIds: [team.Id]);
        await SeedFile(view, "two.txt", teamIds: [team.Id]);

        var files = await Service().GetAsync(Ct);

        Assert.Equal(["one.txt", "two.txt"], files.Select(x => x.Name).Order());
    }

    [Fact]
    public async Task GetAsync_is_forbidden_without_ViewViews()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous()).GetAsync(Ct));
    }

    /// <summary>
    /// Characterizes current behaviour. The system-permission-only overload leaves both team permission
    /// arrays null, and the requirement handler enumerates them for any caller who holds a team claim —
    /// so a view member without <c>ViewViews</c> gets a 500 instead of a 403.
    /// </summary>
    [Fact]
    public async Task GetAsync_throws_for_a_view_member_without_ViewViews()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        await Assert.ThrowsAsync<ArgumentNullException>(() => Service(caller).GetAsync(Ct));
    }

    [Fact]
    public async Task GetByViewAsync_returns_all_of_the_views_files()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.View("Other View");
        await Seed(view, team, other);
        await SeedFile(view, "mine.txt", teamIds: [team.Id]);
        await SeedFile(other, "theirs.txt");

        var files = await Service().GetByViewAsync(view.Id, includeAllViewFiles: true, Ct);

        Assert.Equal("mine.txt", Assert.Single(files).Name);
    }

    [Fact]
    public async Task GetByViewAsync_throws_for_an_unknown_view()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<ViewEntity>>(
            () => Service().GetByViewAsync(Guid.NewGuid(), includeAllViewFiles: true, Ct));
    }

    [Fact]
    public async Task GetByViewAsync_is_forbidden_without_access_to_the_view()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous())
                .GetByViewAsync(view.Id, includeAllViewFiles: true, Ct));
    }

    /// <summary>
    /// Without <c>includeAllViewFiles</c> the answer is assembled per team the caller can see, and a file
    /// assigned to two of them is returned once.
    /// </summary>
    [Fact]
    public async Task GetByViewAsync_returns_a_file_shared_by_two_teams_once()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        var user = TestData.User();
        await Seed(view, first, second, user);
        await SeedFile(view, "shared.txt", teamIds: [first.Id, second.Id]);

        var caller = new ClaimsPrincipalBuilder()
            .WithUserId(user.Id)
            .WithTeam(view.Id, first.Id, isPrimary: true, viewPermissions: [ViewPermission.ViewView])
            .Build();

        var files = await Service(caller).GetByViewAsync(view.Id, includeAllViewFiles: false, Ct);

        Assert.Equal("shared.txt", Assert.Single(files).Name);
    }

    [Fact]
    public async Task GetByTeamAsync_returns_only_the_teams_files()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other Team");
        await Seed(view, team, other);
        await SeedFile(view, "mine.txt", teamIds: [team.Id]);
        await SeedFile(view, "theirs.txt", teamIds: [other.Id]);

        var files = await Service().GetByTeamAsync(team.Id, Ct);

        Assert.Equal("mine.txt", Assert.Single(files).Name);
    }

    [Fact]
    public async Task GetByTeamAsync_is_forbidden_without_access_to_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous()).GetByTeamAsync(team.Id, Ct));
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_file()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        var model = await Service().GetByIdAsync(file.Id, Ct);

        Assert.Equal("notes.txt", model.Name);
        Assert.Equal(view.Id, model.viewId);
    }

    [Fact]
    public async Task GetByIdAsync_throws_for_an_unknown_file()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<FileModel>>(
            () => Service().GetByIdAsync(Guid.NewGuid(), Ct));
    }

    /// <summary>
    /// Access is granted per assigned team, so a file assigned to none is unreachable — even for a caller
    /// holding every system permission.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_is_forbidden_for_a_file_assigned_to_no_team()
    {
        var view = TestData.View();
        await Seed(view);
        var file = await SeedFile(view);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service().GetByIdAsync(file.Id, Ct));
    }

    [Fact]
    public async Task DownloadAsync_returns_the_bytes_under_the_original_name()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", "downloaded", [team.Id]);

        var (stream, name) = await Service().DownloadAsync(file.Id, Ct);

        await using (stream)
        {
            Assert.Equal("notes.txt", name);
            Assert.Equal("downloaded", await new StreamReader(stream).ReadToEndAsync(Ct));
        }
    }

    [Fact]
    public async Task DownloadAsync_throws_for_an_unknown_file()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<FileModel>>(
            () => Service().DownloadAsync(Guid.NewGuid(), Ct));
    }

    // ---- Update -----------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_renames_and_reassigns_the_file()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other Team");
        await Seed(view, team, other);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        var updated = await Service().UpdateAsync(
            file.Id,
            new FileUpdateForm { Name = "renamed.txt", TeamIds = [other.Id] },
            Ct);

        Assert.Equal("renamed.txt", updated.Name);
        Assert.Equal([other.Id], updated.teamIds);
    }

    /// <summary>
    /// Replacing the bytes writes a new file and drops the old one, since nothing else pointed at it.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_replaces_the_file_and_deletes_the_last_pointer()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", "before", [team.Id]);
        var originalPath = file.Path;

        var updated = await Service().UpdateAsync(
            file.Id,
            new FileUpdateForm
            {
                Name = "ignored.txt",
                TeamIds = [team.Id],
                ToUpload = FormFile("replacement.txt", Encoding.UTF8.GetBytes("after"))
            },
            Ct);

        Assert.Equal("replacement.txt", updated.Name);
        Assert.False(File.Exists(originalPath));

        using var db = NewContext();
        Assert.Equal("after", await File.ReadAllTextAsync(db.Files.Single().Path, Ct));
    }

    /// <summary>
    /// Two rows can point at one file — an imported view is copied by pointer — so the bytes survive until
    /// the last row is gone.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_keeps_the_old_file_while_another_pointer_remains()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", "before", [team.Id]);
        await SeedFileAt(view, file.Path, "copy.txt", team.Id);

        await Service().UpdateAsync(
            file.Id,
            new FileUpdateForm
            {
                TeamIds = [team.Id],
                ToUpload = FormFile("replacement.txt", Encoding.UTF8.GetBytes("after"))
            },
            Ct);

        Assert.True(File.Exists(file.Path));
    }

    /// <summary>
    /// Characterizes current behaviour. The rename branch does not check the extension the upload branch
    /// enforces, so a stored file can be given any name — and that name is what a download reports.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_accepts_a_disallowed_extension_when_only_renaming()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        var updated = await Service().UpdateAsync(
            file.Id,
            new FileUpdateForm { Name = "payload.exe", TeamIds = [team.Id] },
            Ct);

        Assert.Equal("payload.exe", updated.Name);
    }

    [Fact]
    public async Task UpdateAsync_refuses_a_disallowed_extension_on_a_replacement()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service().UpdateAsync(
            file.Id,
            new FileUpdateForm { TeamIds = [team.Id], ToUpload = FormFile("payload.exe", [1]) },
            Ct));
    }

    [Fact]
    public async Task UpdateAsync_throws_for_an_unknown_file()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<FileModel>>(() => Service().UpdateAsync(
            Guid.NewGuid(),
            new FileUpdateForm { Name = "renamed.txt", TeamIds = [] },
            Ct));
    }

    [Fact]
    public async Task UpdateAsync_refuses_a_team_from_another_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, team, other, otherTeam);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service().UpdateAsync(
            file.Id,
            new FileUpdateForm { Name = "renamed.txt", TeamIds = [otherTeam.Id] },
            Ct));
    }

    [Fact]
    public async Task UpdateAsync_is_forbidden_for_a_caller_who_can_only_view_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(caller).UpdateAsync(
            file.Id,
            new FileUpdateForm { Name = "renamed.txt", TeamIds = [team.Id] },
            Ct));
    }

    // ---- Delete -----------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_removes_the_row_and_the_file()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        Assert.True(await Service().DeleteAsync(file.Id, Ct));

        Assert.False(File.Exists(file.Path));
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task DeleteAsync_keeps_the_file_while_another_pointer_remains()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);
        var copy = await SeedFileAt(view, file.Path, "copy.txt", team.Id);

        await Service().DeleteAsync(file.Id, Ct);

        Assert.True(File.Exists(file.Path));
        Assert.Equal(copy.Id, Assert.Single(await Stored()).Id);
    }

    /// <summary>
    /// A row with no path is a view's hidden file, which never had bytes of its own — deleting it must not
    /// go looking for them.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_removes_a_row_with_no_path()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFileAt(view, path: null, "hidden.txt", team.Id);

        Assert.True(await Service().DeleteAsync(file.Id, Ct));
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task DeleteAsync_throws_for_an_unknown_file()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<FileModel>>(
            () => Service().DeleteAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task DeleteAsync_is_forbidden_for_a_caller_who_can_only_view_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", teamIds: [team.Id]);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(caller).DeleteAsync(file.Id, Ct));
    }

    // ---- SaveFile ---------------------------------------------------------------------------------

    /// <summary>
    /// The entry point the view importer uses: bytes already in hand rather than an upload.
    /// </summary>
    [Fact]
    public async Task SaveFile_writes_the_bytes_and_returns_their_path()
    {
        var view = TestData.View();
        await Seed(view);

        var entity = new FileEntity { Id = Guid.NewGuid(), Name = "imported.txt", View = view };

        var path = await Service().SaveFile(entity, Encoding.UTF8.GetBytes("imported"), Ct);

        Assert.Equal(Path.Combine(_basePath, view.Id.ToString(), "imported.txt"), path);
        Assert.Equal("imported", await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task SaveFile_refuses_a_disallowed_extension()
    {
        var view = TestData.View();
        await Seed(view);

        var entity = new FileEntity { Id = Guid.NewGuid(), Name = "payload.exe", View = view };

        await Assert.ThrowsAsync<ForbiddenException>(() => Service().SaveFile(entity, [1], Ct));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// The file rows as they are on disk, read through a context that lives only for the read: the
    /// service writes through the context the test holds, and an undisposed context keeps its pooled
    /// connection for the rest of the run.
    /// </summary>
    private async Task<List<FileEntity>> Stored()
    {
        await using var db = NewContext();

        return await db.Files.AsNoTracking().ToListAsync(Ct);
    }

    /// <summary>
    /// The service as <paramref name="user"/>, over this test's upload directory. <paramref name="maxSize"/>
    /// replaces the configured upload limit; left alone, the host's own default stands.
    /// </summary>
    private IFileService Service(ClaimsPrincipal user = null, long? maxSize = null) =>
        HostFor(user ?? Root, options =>
        {
            options.FileUpload.basePath = _basePath;
            options.FileUpload.maxSize = maxSize ?? options.FileUpload.maxSize;
        })
        .Resolve<IFileService>();

    /// <summary>An upload of <paramref name="size"/> bytes, for the tests about the limit.</summary>
    private static FileForm SizedForm(Guid viewId, Guid teamId, int size) =>
        new()
        {
            viewId = viewId,
            teamIds = [teamId],
            ToUpload = [FormFile("notes.txt", new byte[size])]
        };

    private static FileForm Form(Guid viewId, List<Guid> teamIds, params (string Name, string Content)[] files) =>
        new()
        {
            viewId = viewId,
            teamIds = teamIds,
            ToUpload = [.. files.Select(x => FormFile(x.Name, Encoding.UTF8.GetBytes(x.Content)))]
        };

    private static IFormFile FormFile(string name, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "files", name)
        {
            Headers = new HeaderDictionary()
        };

    /// <summary>
    /// A file row whose bytes are on disk, as an upload would have left it.
    /// </summary>
    private async Task<FileEntity> SeedFile(
        ViewEntity view,
        string name = "notes.txt",
        string content = "hello",
        Guid[] teamIds = null)
    {
        var path = Path.Combine(_basePath, view.Id.ToString(), $"{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        await File.WriteAllTextAsync(path, content, Ct);

        return await SeedFileAt(view, path, name, teamIds ?? []);
    }

    /// <summary>
    /// A file row pointing at <paramref name="path"/>, which may be one another row already uses or none
    /// at all.
    /// </summary>
    private async Task<FileEntity> SeedFileAt(ViewEntity view, string path, string name, params Guid[] teamIds)
    {
        var entity = new FileEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            TeamIds = [.. teamIds],
            View = view
        };

        await Seed(entity);
        return entity;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }
}
