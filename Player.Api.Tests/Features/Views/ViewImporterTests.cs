// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Features.Teams;
using Player.Api.Features.Views;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// The validation half of view import, driven through <see cref="ViewImporter"/> directly. Its
/// rejections need either a malformed manifest or ids belonging to a second system — an unresolved
/// template or role is the normal outcome of importing a good export elsewhere — and hand-building a
/// manifest is cheaper than staging two systems.
/// </summary>
/// <remarks>
/// The endpoint's own paths — authorization, a missing <c>views.json</c>, an id that already exists, and
/// the successful round trip — are in <c>ViewRequestTests</c>; the mapping the importer runs on is in
/// <c>ViewExportRoundTripTests</c>.
/// </remarks>
public class ViewImporterTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>
    /// Where the importer's file writes land. Only the file tests reach the disk, and what they assert
    /// is which directory under here the bytes end up in.
    /// </summary>
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), $"player-import-tests-{Guid.NewGuid():N}");

    // ---- Scoped team permissions ----------------------------------------------------------------

    [Fact]
    public async Task Scoped_team_permissions_are_imported()
    {
        var blue = Team("Blue");
        var red = Team("Red");
        blue.ScopedTeamIds = [red.Id];

        var view = Export();
        view.Teams = [blue, red];

        Assert.Empty(await Import([view]));

        await using var db = NewContext();
        var scope = await db.TeamPermissionScopes.SingleAsync(Ct);
        Assert.Equal(blue.Id, scope.TeamId);
        Assert.Equal(red.Id, scope.TargetTeamId);
    }

    [Fact]
    public async Task A_team_that_scopes_its_permissions_onto_itself_is_rejected()
    {
        var blue = Team("Blue");
        blue.ScopedTeamIds = [blue.Id];

        var view = Export();
        view.Teams = [blue];

        var failure = Assert.Single(await Import([view]));

        Assert.Equal(view.Id, failure.Id);
        Assert.Equal(view.Name, failure.Name);
        Assert.Equal(ImportViewFailureType.Other, failure.FailureType);
        Assert.Equal(
            $"Team Blue ({blue.Id}): A Team cannot scope its permissions onto itself",
            failure.Reason);
        await AssertNotImported(view);
    }

    [Fact]
    public async Task A_team_with_duplicate_scoped_team_ids_is_rejected()
    {
        var blue = Team("Blue");
        var red = Team("Red");
        blue.ScopedTeamIds = [red.Id, red.Id];

        var view = Export();
        view.Teams = [blue, red];

        var failure = Assert.Single(await Import([view]));

        Assert.Equal($"Team Blue ({blue.Id}): Duplicate scoped Team ids", failure.Reason);
        await AssertNotImported(view);
    }

    /// <summary>
    /// A scope is a relationship between two teams of one view, so an id from outside the manifest can
    /// only be a leftover from the system that produced it.
    /// </summary>
    [Fact]
    public async Task A_team_scoped_onto_a_team_outside_the_view_is_rejected()
    {
        var stranger = Guid.NewGuid();
        var blue = Team("Blue");
        blue.ScopedTeamIds = [stranger];

        var view = Export();
        view.Teams = [blue];

        var failure = Assert.Single(await Import([view]));

        Assert.Equal(
            $"Team Blue ({blue.Id}): Scoped Team {stranger} is not part of the imported View",
            failure.Reason);
        await AssertNotImported(view);
    }

    /// <summary>
    /// The property defaults to an empty list, so only an explicit <c>null</c> in a manifest produces one
    /// — which both the validator and the entity build tolerate.
    /// </summary>
    [Fact]
    public async Task A_team_whose_scoped_team_ids_are_null_is_imported()
    {
        var blue = Team("Blue");
        blue.ScopedTeamIds = null;

        var view = Export();
        view.Teams = [blue];

        Assert.Empty(await Import([view]));

        await using var db = NewContext();
        Assert.Equal("Blue", (await db.Teams.SingleAsync(x => x.ViewId == view.Id, Ct)).Name);
        Assert.Empty(await db.TeamPermissionScopes.ToListAsync(Ct));
    }

    // ---- Application templates ------------------------------------------------------------------

    /// <summary>
    /// The name is only a fallback: an application carrying both a resolvable id and a name that matches
    /// a different template reaches the one it was exported against.
    /// </summary>
    [Fact]
    public async Task An_application_template_id_wins_over_a_matching_name()
    {
        var exportedAgainst = TestData.ApplicationTemplate("Exported Against");
        var sharesTheName = TestData.ApplicationTemplate("Renamed Locally");
        await Seed(exportedAgainst, sharesTheName);

        var view = Export();
        view.Applications =
            [Application(view.Id, templateId: exportedAgainst.Id, templateName: "Renamed Locally")];

        Assert.Empty(await Import([view], matchApplicationTemplatesByName: true));

        await using var db = NewContext();
        var imported = await db.Applications.SingleAsync(x => x.ViewId == view.Id, Ct);
        Assert.Equal(exportedAgainst.Id, imported.ApplicationTemplateId);
    }

    /// <summary>
    /// The id in a manifest belongs to the system that exported it, so name matching is what makes an
    /// import into a second system work at all. The template id is rewritten to the local one.
    /// </summary>
    /// <remarks>
    /// The decoy is seeded first so that matching on position rather than on the name would pick it.
    /// </remarks>
    [Fact]
    public async Task An_application_template_is_matched_by_name_when_the_caller_asks()
    {
        await Seed(TestData.ApplicationTemplate("Decoy Template"));
        var template = TestData.ApplicationTemplate("Local Template");
        await Seed(template);

        var view = Export();
        view.Applications =
            [Application(view.Id, templateId: Guid.NewGuid(), templateName: "Local Template")];

        Assert.Empty(await Import([view], matchApplicationTemplatesByName: true));

        await using var db = NewContext();
        var imported = await db.Applications.SingleAsync(x => x.ViewId == view.Id, Ct);
        Assert.Equal(template.Id, imported.ApplicationTemplateId);
    }

    /// <summary>
    /// Same manifest and same local template as the test above, with the flag off: the name is not
    /// consulted, so the unknown id is a failure.
    /// </summary>
    [Fact]
    public async Task An_application_template_name_is_ignored_unless_the_caller_asks()
    {
        await Seed(TestData.ApplicationTemplate("Local Template"));

        var view = Export();
        var app = Application(view.Id, templateId: Guid.NewGuid(), templateName: "Local Template");
        view.Applications = [app];

        var failure = Assert.Single(await Import([view]));

        Assert.Equal(
            $"Application Console ({app.Id}): No matching Application Template found",
            failure.Reason);
        await AssertNotImported(view);
    }

    /// <summary>
    /// An application that names no template is a URL of its own, not a broken reference, so it is
    /// skipped rather than rejected.
    /// </summary>
    [Fact]
    public async Task An_application_with_no_template_is_imported_without_validation()
    {
        var view = Export();
        view.Applications = [Application(view.Id, name: "Standalone")];

        Assert.Empty(await Import([view]));

        await using var db = NewContext();
        var imported = await db.Applications.SingleAsync(x => x.ViewId == view.Id, Ct);
        Assert.Equal("Standalone", imported.Name);
        Assert.Null(imported.ApplicationTemplateId);
    }

    // ---- Files ----------------------------------------------------------------------------------

    /// <summary>
    /// Files are matched to archive entries by id and name, so an archive missing one is reported rather
    /// than importing a row that points at nothing.
    /// </summary>
    [Fact]
    public async Task A_file_the_archive_does_not_contain_is_rejected()
    {
        var view = Export();
        var file = ExportedFile(view.Id, "notes.txt");
        view.Files = [file];

        var failure = Assert.Single(await Import([view]));

        Assert.Equal(
            $"File notes.txt ({file.id}): No matching file found in archive",
            failure.Reason);
        await AssertNotImported(view);
    }

    /// <summary>
    /// The upload rules apply to imported bytes too, and a rejected file is one view's failure rather
    /// than a 500 for the whole archive.
    /// </summary>
    [Fact]
    public async Task A_file_the_upload_options_reject_is_reported_rather_than_thrown()
    {
        var view = Export();
        var file = ExportedFile(view.Id, "payload.exe");
        view.Files = [file];

        var failure = Assert.Single(await Import([view], Archive(file)));

        Assert.Equal($"File payload.exe ({file.id}): Invalid file extension", failure.Reason);
        await AssertNotImported(view);
    }

    /// <summary>
    /// Files are the only part of an archive that reaches the disk, and the only part whose entity is
    /// reached through a navigation rather than a foreign key — so this covers both: the row points at
    /// the imported view, and the bytes are written.
    /// </summary>
    /// <remarks>
    /// Import keeps the file's own name, where upload stores under a generated one, so an imported path
    /// is predictable from the manifest.
    /// </remarks>
    [Fact]
    public async Task A_view_whose_file_is_in_the_archive_is_imported_with_its_bytes()
    {
        var view = Export();
        var file = ExportedFile(view.Id, "notes.txt");
        view.Files = [file];

        Assert.Empty(await Import([view], Archive(file, "imported bytes")));

        await using var db = NewContext();
        var stored = await db.Files.Include(x => x.View).SingleAsync(Ct);
        Assert.Equal(file.id, stored.Id);
        Assert.Equal("notes.txt", stored.Name);
        Assert.Equal(view.Id, stored.View.Id);
        Assert.Equal(Path.Combine(_basePath, view.Id.ToString(), "notes.txt"), stored.Path);
        Assert.Equal("imported bytes", await File.ReadAllTextAsync(stored.Path, Ct));
    }

    /// <summary>
    /// Which directory the bytes land in is decided by the <c>viewId</c> in the manifest, not by the view
    /// that ends up owning the row: the write happens through the placeholder view AutoMapper builds
    /// while unflattening, before EF replaces it with the real one. So the upload directory is not
    /// partitioned by owner the way it looks — harmless, because the stored path is absolute.
    /// </summary>
    /// <remarks>
    /// The single view row is the other half: the placeholder is discarded rather than imported.
    /// </remarks>
    [Fact]
    public async Task A_file_naming_a_different_view_is_imported_but_written_under_that_id()
    {
        var stranger = Guid.NewGuid();
        var view = Export();
        var file = ExportedFile(stranger, "notes.txt");
        view.Files = [file];

        Assert.Empty(await Import([view], Archive(file)));

        await using var db = NewContext();
        var stored = await db.Files.Include(x => x.View).SingleAsync(Ct);
        Assert.Equal(view.Id, stored.View.Id);
        Assert.Equal(Path.Combine(_basePath, stranger.ToString(), "notes.txt"), stored.Path);
        Assert.Single(await db.Views.ToListAsync(Ct));
    }

    /// <summary>
    /// Characterizes issue 47: each file is written as it is validated, so a later file the archive does not
    /// contain rejects the view after the earlier bytes are already on disk, leaving them with no row that
    /// owns them and no way to delete them through the API.
    /// </summary>
    /// <remarks>
    /// Intra-view only — a view that passes validation is saved and legitimately owns its bytes. Turns
    /// red when the importer defers its writes to the save, or removes what it wrote for a rejected view.
    /// </remarks>
    [Fact]
    public async Task A_rejected_view_leaves_behind_the_files_it_already_wrote()
    {
        var view = Export();
        var inTheArchive = ExportedFile(view.Id, "one.txt");
        var missing = ExportedFile(view.Id, "two.txt");
        view.Files = [inTheArchive, missing];

        var failure = Assert.Single(await Import([view], Archive(inTheArchive)));

        Assert.Equal(
            $"File two.txt ({missing.id}): No matching file found in archive",
            failure.Reason);

        await using var db = NewContext();
        Assert.Empty(await db.Views.ToListAsync(Ct));
        Assert.True(File.Exists(Path.Combine(_basePath, view.Id.ToString(), "one.txt")));
    }

    /// <summary>
    /// Issue 47: the write happens whatever the view was rejected for. Validation accumulates failures and
    /// only decides at the end, so a manifest condemned by its applications still writes every file it
    /// carries — nothing here is about files at all.
    /// </summary>
    /// <remarks>Turns red once the importer defers its writes past the accept decision.</remarks>
    [Fact]
    public async Task A_view_rejected_for_a_reason_other_than_its_files_still_writes_them()
    {
        var view = Export();
        var app = Application(view.Id, templateId: Guid.NewGuid(), templateName: "Local Template");
        view.Applications = [app];
        var file = ExportedFile(view.Id, "notes.txt");
        view.Files = [file];

        var failure = Assert.Single(await Import([view], Archive(file)));

        Assert.Equal(
            $"Application Console ({app.Id}): No matching Application Template found",
            failure.Reason);

        await using var db = NewContext();
        Assert.Empty(await db.Views.ToListAsync(Ct));
        Assert.Empty(await db.Files.ToListAsync(Ct));

        // Bytes with no row that owns them, under a view id that does not exist.
        Assert.True(File.Exists(Path.Combine(_basePath, view.Id.ToString(), "notes.txt")));
    }

    // ---- The archive as a batch -----------------------------------------------------------------

    /// <summary>
    /// Failures are per view, so an archive of many views keeps the ones that were fine.
    /// </summary>
    [Fact]
    public async Task One_rejected_view_does_not_stop_the_rest_of_the_archive()
    {
        var blue = Team("Blue");
        blue.ScopedTeamIds = [Guid.NewGuid()];

        var rejected = Export("Rejected");
        rejected.Teams = [blue];

        var accepted = Export("Accepted");
        accepted.Teams = [Team("Red")];

        var failure = Assert.Single(await Import([rejected, accepted]));

        Assert.Equal(rejected.Id, failure.Id);

        await using var db = NewContext();
        Assert.Equal("Accepted", (await db.Views.SingleAsync(Ct)).Name);
    }

    /// <summary>
    /// Characterizes issue 17: <c>ParentViewId</c> is mapped straight through and never validated, so a
    /// child view imported without its parent violates a foreign key. Every other view in the archive
    /// goes with it, because the importer saves the whole batch once.
    /// </summary>
    /// <remarks>
    /// Turns red when the importer validates the parent: expect an <c>ImportViewFailure</c> naming it,
    /// and "Sibling" imported. PostgreSQL-only because it needs the foreign key to be enforced, which
    /// <c>DatabaseHarnessTests.The_sqlite_fallback_enforces_foreign_keys</c> records for the fallback.
    /// </remarks>
    [RequiresPostgres]
    public async Task A_view_whose_parent_is_missing_fails_the_whole_import()
    {
        var orphan = Export("Orphan");
        orphan.ParentViewId = Guid.NewGuid();

        var sibling = Export("Sibling");

        await Assert.ThrowsAsync<DbUpdateException>(() => Import([orphan, sibling]));

        await using var db = NewContext();
        Assert.Empty(await db.Views.ToListAsync(Ct));
    }

    /// <summary>
    /// Characterizes an unfiled bug: role validation only looks at teams that carry a role id or a role
    /// name, so a team with neither is never checked; its required role foreign key is then
    /// <see cref="Guid.Empty"/> and the batch's single save violates it, taking the other views with it.
    /// </summary>
    /// <remarks>
    /// Reachable from hand-written seed data, where it fails startup with a raw constraint violation
    /// rather than the named seed failure. Turns red when the importer rejects a team with no role:
    /// expect an <c>ImportViewFailure</c> naming it, and "Sibling" imported.
    /// </remarks>
    [RequiresPostgres]
    public async Task A_team_with_no_role_fails_the_whole_import()
    {
        var roleless = Export("Roleless");
        roleless.Teams = [new TeamExport { Id = Guid.NewGuid(), Name = "Blue" }];

        var sibling = Export("Sibling");

        await Assert.ThrowsAsync<DbUpdateException>(() => Import([roleless, sibling]));

        await using var db = NewContext();
        Assert.Empty(await db.Views.ToListAsync(Ct));
    }

    /// <summary>
    /// Characterizes issue 39: application validation walks the array without a null check, so a manifest
    /// that omits it fails LINQ's own argument guard rather than being reported.
    /// </summary>
    /// <remarks>
    /// Turns red when the importer normalizes the array: expect the view to import with no applications.
    /// </remarks>
    [Fact]
    public async Task A_manifest_missing_its_Applications_array_throws()
    {
        var view = Export();
        view.Applications = null;

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => Import([view]));

        // LINQ's parameter, not a member of the manifest: this is Where's guard, not a dereference.
        Assert.Equal("source", ex.ParamName);
    }

    /// <summary>
    /// The other half of issue 39, from the other validator. A missing <c>Files</c> array is safe by
    /// contrast, because file validation walks the mapped entity, whose collection is never null.
    /// </summary>
    /// <remarks>
    /// Turns red when the importer normalizes the array: expect the view to import with no teams.
    /// </remarks>
    [Fact]
    public async Task A_manifest_missing_its_Teams_array_throws()
    {
        var view = Export();
        view.Teams = null;

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => Import([view]));

        Assert.Equal("source", ex.ParamName);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// The importer over this test's upload directory. Resolved from the host so it gets the real mapper
    /// and the real <c>FileService</c>, as the endpoint's handler does.
    /// </summary>
    private ViewImporter Importer =>
        HostFor(Root, options => options.FileUpload.basePath = _basePath).Resolve<ViewImporter>();

    /// <summary>
    /// Defaults match the endpoint's, whose command leaves both flags false. The seed-data importer is
    /// the only caller that turns them on, and it turns on both.
    /// </summary>
    private Task<IEnumerable<ImportViewFailure>> Import(
        ViewExport[] views,
        Dictionary<string, byte[]> fileData = null,
        bool matchApplicationTemplatesByName = false,
        bool matchRolesByName = false) =>
        Importer.Import(views, fileData ?? [], matchApplicationTemplatesByName, matchRolesByName, Ct);

    /// <summary>
    /// A minimal importable view. <see cref="ViewEntity.DateCreated"/> is a
    /// <c>timestamp with time zone</c>, which PostgreSQL refuses to accept from an unspecified kind.
    /// </summary>
    private static ViewExport Export(string name = "Imported View") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = ViewStatus.Active,
            DateCreated = TestData.DefaultDateCreated,
            Teams = [],
            Applications = [],
            Files = []
        };

    /// <summary>
    /// A team carrying a seeded role id, since <see cref="TeamEntity.RoleId"/> is a required foreign key
    /// and an unresolved role is a different test's subject.
    /// </summary>
    private static TeamExport Team(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            RoleId = TestData.TeamRoles.ViewMember
        };

    private static ApplicationExport Application(
        Guid viewId,
        string name = "Console",
        Guid? templateId = null,
        string templateName = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = "https://example.test/app",
            ViewId = viewId,
            ApplicationTemplateId = templateId,
            ApplicationTemplateName = templateName
        };

    private static FileModel ExportedFile(Guid viewId, string name) =>
        new()
        {
            id = Guid.NewGuid(),
            Name = name,
            viewId = viewId,
            teamIds = []
        };

    /// <summary>
    /// One archive entry, keyed the way <c>Export</c> names it.
    /// </summary>
    private static Dictionary<string, byte[]> Archive(FileModel file, string content = "hello") =>
        new() { [$"{file.id}-{file.Name}"] = Encoding.UTF8.GetBytes(content) };

    /// <summary>
    /// No view row is written for a view that failed validation, which is what makes a per-view failure
    /// safe to report and continue from. The disk is the exception — see
    /// <see cref="A_rejected_view_leaves_behind_the_files_it_already_wrote"/>.
    /// </summary>
    private async Task AssertNotImported(ViewExport view)
    {
        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
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
