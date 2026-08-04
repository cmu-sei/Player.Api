// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// Covers the <c>Views</c> feature's request handlers end to end: real handler, real authorization,
/// real AutoMapper profiles, real database.
/// </summary>
public class ViewRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    /// <summary>Only the file round-trip test writes here; the rest of the file never touches disk.</summary>
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), $"player-view-tests-{Guid.NewGuid():N}");

    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_view_and_returns_it()
    {
        var created = await SendAsync(new Create.Command
        {
            Name = "Exercise",
            Description = "A description",
            Status = ViewStatus.Inactive,
            IsTemplate = true,
            CreateAdminTeam = false
        });

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Exercise", created.Name);
        Assert.Equal(ViewStatus.Inactive, created.Status);
        Assert.True(created.IsTemplate);

        await using var db = NewContext();
        Assert.Equal("Exercise", (await db.Views.SingleAsync(x => x.Id == created.Id, Ct)).Name);
    }

    [Fact]
    public async Task Create_honors_a_caller_supplied_id()
    {
        var id = Guid.NewGuid();

        var created = await SendAsync(new Create.Command { Name = "Fixed", Id = id, CreateAdminTeam = false });

        Assert.Equal(id, created.Id);
    }

    /// <summary>
    /// An explicitly empty id means "assign one", not "use Guid.Empty" — no serializer lets the request
    /// model tell an absent id from a defaulted one, so the handler treats them alike.
    /// </summary>
    [Fact]
    public async Task Create_assigns_an_id_when_the_caller_sends_an_empty_one()
    {
        var created = await SendAsync(new Create.Command
        {
            Name = "Empty id",
            Id = Guid.Empty,
            CreateAdminTeam = false
        });

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    /// <summary>
    /// Creating a view is what makes its creator an administrator of it: an Admin team, a view
    /// membership, a team membership, and that membership marked primary. None of the four shows in the
    /// response body.
    /// </summary>
    [Fact]
    public async Task Create_enrolls_the_creator_in_a_new_admin_team_by_default()
    {
        // The memberships are foreign-keyed to the caller's user row, which the claims transformation
        // creates before a real request reaches a handler.
        await Seed(TestData.User(RootHost.UserId));

        var created = await SendAsync(new Create.Command { Name = "With admins" });

        await using var db = NewContext();
        var team = await db.Teams.SingleAsync(x => x.ViewId == created.Id, Ct);
        Assert.Equal("Admin", team.Name);

        var viewMembership = await db.ViewMemberships
            .SingleAsync(x => x.ViewId == created.Id && x.UserId == RootHost.UserId, Ct);
        var teamMembership = await db.TeamMemberships
            .SingleAsync(x => x.TeamId == team.Id && x.UserId == RootHost.UserId, Ct);

        Assert.Equal(viewMembership.Id, teamMembership.ViewMembershipId);
        Assert.Equal(teamMembership.Id, viewMembership.PrimaryTeamMembershipId);
    }

    /// <summary>
    /// <c>RoleOptions.DefaultViewCreatorRole</c> is resolved with <c>SingleAsync</c>, so a name matching
    /// no seeded role fails the request rather than creating a view nobody can administer.
    /// </summary>
    [Fact]
    public async Task Create_fails_when_the_configured_view_creator_role_does_not_exist()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.CreateViews)
            .Build();
        var host = HostFor(user, options => options.Roles.DefaultViewCreatorRole = "No Such Role");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Mediator.Send(new Create.Command { Name = "Doomed" }, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_without_CreateViews()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewViews)
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(user, new Create.Command { Name = "Nope", CreateAdminTeam = false }));
    }

    // ---- Get ------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_view()
    {
        var view = TestData.View("Findable");
        await Seed(view);

        Assert.Equal("Findable", (await SendAsync(new Get.Query { Id = view.Id })).Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<View>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// A member of the view may read it without <c>ViewViews</c>: the second clause of
    /// <c>Authorize</c> falls back to the view ids in the caller's team claims.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_member_of_the_view_without_ViewViews()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var user = new ClaimsPrincipalBuilder().WithTeam(view.Id, team.Id).Build();

        Assert.Equal(view.Id, (await SendAsync(user, new Get.Query { Id = view.Id })).Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_member_of_a_different_view()
    {
        var view = TestData.View();
        await Seed(view);

        var user = new ClaimsPrincipalBuilder().WithTeam(Guid.NewGuid(), Guid.NewGuid()).Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(user, new Get.Query { Id = view.Id }));
    }

    // ---- GetAll ---------------------------------------------------------------------------------

    /// <summary>
    /// Unfiltered by design: the permission is global, so the query returns every view rather than
    /// scoping to the caller's memberships.
    /// </summary>
    [Fact]
    public async Task GetAll_returns_every_view()
    {
        await Seed(TestData.View("One"), TestData.View("Two"));

        var views = await SendAsync(new GetAll.Query());

        Assert.Equal(2, views.Length);
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_ViewViews()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetAll.Query()));
    }

    // ---- GetByUser ------------------------------------------------------------------------------

    [Fact]
    public async Task GetByUser_returns_only_the_views_the_user_is_a_member_of()
    {
        var member = TestData.View("Member of");
        var other = TestData.View("Not a member of");
        var user = TestData.User();
        await Seed(member, other, user, TestData.ViewMembership(member.Id, user.Id));

        var views = await SendAsync(new GetByUser.Query { UserId = user.Id });

        Assert.Equal("Member of", Assert.Single(views).Name);
    }

    [Fact]
    public async Task GetByUser_reports_a_missing_user_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Users.User>>(
            () => SendAsync(new GetByUser.Query { UserId = Guid.NewGuid() }));
    }

    /// <summary>
    /// Callers may always ask about themselves. The identity check short-circuits before the
    /// permission check, so no permission is needed at all.
    /// </summary>
    [Fact]
    public async Task GetByUser_is_allowed_for_the_caller_asking_about_themselves()
    {
        var builder = new ClaimsPrincipalBuilder();
        var user = TestData.User(builder.UserId);
        await Seed(user);

        Assert.Empty(await SendAsync(builder.Build(), new GetByUser.Query { UserId = user.Id }));
    }

    [Fact]
    public async Task GetByUser_is_forbidden_for_another_user_without_ViewUsers()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetByUser.Query { UserId = Guid.NewGuid() }));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_updates_the_view()
    {
        var view = TestData.View("Before");
        await Seed(view);

        var edited = await SendAsync(new Edit.Command
        {
            Id = view.Id,
            Name = "After",
            Description = "Now described",
            Status = ViewStatus.Inactive,
            IsTemplate = true
        });

        Assert.Equal("After", edited.Name);
        Assert.Equal(ViewStatus.Inactive, edited.Status);

        await using var db = NewContext();
        Assert.Equal("After", (await db.Views.SingleAsync(x => x.Id == view.Id, Ct)).Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<View>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_view_member_without_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(user, new Edit.Command { Id = view.Id, Name = "Nope" }));
    }

    [Fact]
    public async Task Edit_is_allowed_for_a_view_member_holding_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var edited = await SendAsync(user, new Edit.Command { Id = view.Id, Name = "Allowed" });

        Assert.Equal("Allowed", edited.Name);
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_view()
    {
        var view = TestData.View();
        await Seed(view);

        await SendAsync(new Delete.Command { Id = view.Id });

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
    }

    /// <summary>
    /// A view membership points at a team membership the cascade would also delete, so the handler
    /// clears the pointer first. Without that the delete fails on the foreign key.
    /// </summary>
    [Fact]
    public async Task Delete_removes_a_view_whose_memberships_have_a_primary_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);

        // Two saves: the rows point at each other, a cycle EF refuses to order. Production has the same
        // constraint, which is why Create makes two passes.
        await Seed(view, team, user, viewMembership, teamMembership);
        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await SendAsync(new Delete.Command { Id = view.Id });

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
        Assert.False(await db.TeamMemberships.AnyAsync(x => x.Id == teamMembership.Id, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<View>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageViews()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new Delete.Command { Id = view.Id }));
    }

    // ---- Clone ----------------------------------------------------------------------------------

    [Fact]
    public async Task Clone_copies_the_view_and_its_teams()
    {
        var view = TestData.View("Original");
        view.Description = "Original description";
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);

        var clone = await SendAsync(new Clone.Command { ViewId = view.Id });

        Assert.NotEqual(view.Id, clone.Id);
        Assert.Equal("Clone of Original", clone.Name);
        Assert.Equal("Original description", clone.Description);
        Assert.Equal(ViewStatus.Active, clone.Status);

        await using var db = NewContext();
        Assert.Equal("Alpha", (await db.Teams.SingleAsync(x => x.ViewId == clone.Id, Ct)).Name);
    }

    [Fact]
    public async Task Clone_uses_the_supplied_name_and_description_when_given()
    {
        var view = TestData.View("Original");
        view.Description = "Original description";
        await Seed(view);

        var clone = await SendAsync(new Clone.Command
        {
            ViewId = view.Id,
            Name = "Renamed",
            Description = "Redescribed",
            IsTemplate = true
        });

        Assert.Equal("Renamed", clone.Name);
        Assert.Equal("Redescribed", clone.Description);
        Assert.True(clone.IsTemplate);
    }

    /// <summary>
    /// Whitespace is treated as absent, so a client sending an empty form field gets the
    /// "Clone of ..." default rather than a view with a blank name.
    /// </summary>
    [Fact]
    public async Task Clone_ignores_a_whitespace_name()
    {
        var view = TestData.View("Original");
        await Seed(view);

        var clone = await SendAsync(new Clone.Command { ViewId = view.Id, Name = "   " });

        Assert.Equal("Clone of Original", clone.Name);
    }

    /// <summary>
    /// The clone's default team is its own copy, matched by name. Keeping the original's team id would
    /// leave the new view referencing a team in another view.
    /// </summary>
    [Fact]
    public async Task Clone_repoints_the_default_team_at_the_cloned_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Default team");
        await Seed(view, team);

        view.DefaultTeamId = team.Id;
        await Db.SaveChangesAsync(Ct);

        var clone = await SendAsync(new Clone.Command { ViewId = view.Id });

        await using var db = NewContext();
        var clonedTeam = await db.Teams.SingleAsync(x => x.ViewId == clone.Id, Ct);

        Assert.Equal(clonedTeam.Id, clone.DefaultTeamId);
        Assert.NotEqual(team.Id, clone.DefaultTeamId);
    }

    /// <summary>
    /// A scope relates two teams in the same view, so cloning remaps both ends — on a second pass, once
    /// the clone's teams exist.
    /// </summary>
    [Fact]
    public async Task Clone_remaps_scoped_team_permissions_onto_the_cloned_teams()
    {
        var view = TestData.View();
        var source = TestData.Team(view.Id, "Source");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, source, target, TestData.TeamPermissionScope(source.Id, target.Id));

        var clone = await SendAsync(new Clone.Command { ViewId = view.Id });

        await using var db = NewContext();
        var clonedSource = await db.Teams.SingleAsync(x => x.ViewId == clone.Id && x.Name == "Source", Ct);
        var clonedTarget = await db.Teams.SingleAsync(x => x.ViewId == clone.Id && x.Name == "Target", Ct);

        var scope = await db.TeamPermissionScopes.SingleAsync(x => x.TeamId == clonedSource.Id, Ct);
        Assert.Equal(clonedTarget.Id, scope.TargetTeamId);
    }

    [Fact]
    public async Task Clone_is_forbidden_without_CreateViews()
    {
        var view = TestData.View();
        await Seed(view);

        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewViews)
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(user, new Clone.Command { ViewId = view.Id }));
    }

    // ---- Notifications --------------------------------------------------------------------------

    [Fact]
    public async Task SendNotification_persists_it_and_broadcasts_to_the_view_group()
    {
        var view = TestData.View("Broadcast target");
        await Seed(view);

        var result = await SendAsync(new SendNotification.Command
        {
            ViewId = view.Id,
            Subject = "Heads up",
            Text = "Something happened"
        });

        Assert.Contains(view.Id.ToString(), result);

        await using var db = NewContext();
        var notification = await db.Notifications.SingleAsync(Ct);
        Assert.Equal("Something happened", notification.Text);
        Assert.Equal(view.Id, notification.ToId);
        Assert.Equal(NotificationType.View, notification.ToType);
        Assert.Equal(RootHost.UserId, notification.FromId);

        RootHost.ViewHub.Clients.Received().Group(view.Id.ToString());
    }

    /// <summary>
    /// Rejected before anything is persisted, since broadcasting it would push a blank row to every
    /// connected client.
    /// </summary>
    [Fact]
    public async Task SendNotification_rejects_a_notification_with_no_text()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ArgumentException>(() => SendAsync(new SendNotification.Command
        {
            ViewId = view.Id,
            Subject = "No body"
        }));

        await using var db = NewContext();
        Assert.False(await db.Notifications.AnyAsync(Ct));
    }

    [Fact]
    public async Task GetNotifications_returns_the_view_notifications_newest_first()
    {
        var view = TestData.View();
        await Seed(view);

        await SendAsync(new SendNotification.Command { ViewId = view.Id, Text = "First" });
        await SendAsync(new SendNotification.Command { ViewId = view.Id, Text = "Second" });

        var notifications = await SendAsync(new GetNotifications.Command { ViewId = view.Id });

        Assert.Equal(2, notifications.Length);
        Assert.True(notifications[0].BroadcastTime >= notifications[1].BroadcastTime);
    }

    /// <summary>
    /// Neither provider preserves <see cref="DateTimeKind"/>, so the service re-applies it. Without
    /// that, clients render every broadcast time as local.
    /// </summary>
    [Fact]
    public async Task GetNotifications_returns_broadcast_times_as_UTC()
    {
        var view = TestData.View();
        await Seed(view);
        await SendAsync(new SendNotification.Command { ViewId = view.Id, Text = "Timed" });

        var notification = Assert.Single(await SendAsync(new GetNotifications.Command { ViewId = view.Id }));

        Assert.Equal(DateTimeKind.Utc, notification.BroadcastTime.Kind);
    }

    [Fact]
    public async Task DeleteNotification_removes_it_and_tells_the_view_group()
    {
        var view = TestData.View();
        await Seed(view);
        await SendAsync(new SendNotification.Command { ViewId = view.Id, Text = "Doomed" });

        var key = (await SendAsync(new GetNotifications.Command { ViewId = view.Id })).Single().Key;

        var result = await SendAsync(new DeleteNotification.Command { ViewId = view.Id, Key = key });

        Assert.Contains(key.ToString(), result);

        await using var db = NewContext();
        Assert.False(await db.Notifications.AnyAsync(Ct));
        RootHost.ViewHub.Clients.Received().Group(view.Id.ToString());
    }

    [Fact]
    public async Task DeleteAllNotifications_removes_every_notification_for_the_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Untouched");
        await Seed(view, otherView);

        await SendAsync(new SendNotification.Command { ViewId = view.Id, Text = "One" });
        await SendAsync(new SendNotification.Command { ViewId = view.Id, Text = "Two" });
        await SendAsync(new SendNotification.Command { ViewId = otherView.Id, Text = "Other" });

        await SendAsync(new DeleteAllNotifications.Command { ViewId = view.Id });

        await using var db = NewContext();
        Assert.Equal("Other", (await db.Notifications.SingleAsync(Ct)).Text);
    }

    [Fact]
    public async Task GetNotifications_is_forbidden_for_a_non_member_without_ViewViews()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetNotifications.Command { ViewId = view.Id }));
    }

    // ---- Export ---------------------------------------------------------------------------------

    [Fact]
    public async Task Export_produces_an_archive_containing_the_view_json()
    {
        var view = TestData.View("Exported");
        await Seed(view);

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        Assert.False(archive.HasErrors);
        Assert.True(archive.Data.Length > 0);
        Assert.EndsWith(".zip", archive.Name);
    }

    /// <summary>
    /// An empty id array means "everything", not "nothing" — the handler only narrows the query when
    /// ids were supplied.
    /// </summary>
    [Fact]
    public async Task Export_with_no_ids_exports_every_view()
    {
        await Seed(TestData.View("One"), TestData.View("Two"));

        var archive = await SendAsync(new Export.Query { Ids = [], ArchiveType = ArchiveType.zip });

        var views = ArchiveHelper.ReadExportedViews(archive, RootHost.Resolve<Player.Api.Services.IArchiveService>());
        Assert.Equal(2, views.Length);
    }

    /// <summary>
    /// The reason issue 42's tgz size bug does not reach view export: this handler serializes with the
    /// default encoder (<c>Export.cs:135</c>), which escapes every non-ASCII character to <c>\uXXXX</c>, so
    /// <c>views.json</c> is always ASCII and its char count always equals its byte count. Template export
    /// opts out of that escaping and does fail. Goes red if an <c>Encoder</c> is ever set here.
    /// </summary>
    [Fact]
    public async Task Export_as_tgz_survives_a_view_name_that_is_not_ascii()
    {
        var view = TestData.View("Übung");
        await Seed(view);

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.tgz });

        var files = ArchiveHelper.ExtractFiles(archive, RootHost.Resolve<Player.Api.Services.IArchiveService>());
        var json = files[ViewConstants.ExportFileName];

        Assert.All(json, b => Assert.True(b < 0x80));
        Assert.Contains("\\u00DC", Encoding.UTF8.GetString(json));

        // Escaping is a wire detail, not a data loss — it decodes back to the name that was exported.
        var views = ArchiveHelper.ReadExportedViews(archive, RootHost.Resolve<Player.Api.Services.IArchiveService>());
        Assert.Equal("Übung", Assert.Single(views).Name);
    }

    [Fact]
    public async Task Export_round_trips_through_Import()
    {
        var view = TestData.View("Round trip");
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        // The exported view keeps its id, so it has to be gone before the import will accept it.
        await SendAsync(new Delete.Command { Id = view.Id });

        var result = await SendAsync(new Import.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive),
            MatchRolesByName = true
        });

        Assert.Empty(result.Failures);

        await using var db = NewContext();
        var imported = await db.Views.Include(x => x.Teams).SingleAsync(x => x.Id == view.Id, Ct);
        Assert.Equal("Round trip", imported.Name);
        Assert.Equal("Alpha", imported.Teams.Single().Name);
    }

    /// <summary>
    /// The only test that the exporter's archive key and the importer's lookup agree — `Export.cs:122`
    /// writes <c>{id}-{name}</c> and <c>ViewImporter.cs:168</c> reads it back, with nothing shared
    /// between them. A rename on either side turns this red and leaves every hand-built archive green.
    /// </summary>
    [Fact]
    public async Task Export_round_trips_a_file_through_Import_with_its_bytes()
    {
        var view = TestData.View("Round trip with a file");
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);

        // Builds the host, so it has to come before the first SendAsync — configure is honored only once.
        var files = HostFor(Root, options => options.FileUpload.basePath = _basePath)
            .Resolve<Player.Api.Services.IFileService>();

        await files.UploadAsync(
            new Player.Api.ViewModels.FileForm
            {
                viewId = view.Id,
                teamIds = [team.Id],
                ToUpload = [ArchiveHelper.AsFormFile("notes.txt", "exported bytes")]
            },
            Ct);

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        await SendAsync(new Delete.Command { Id = view.Id });

        var result = await SendAsync(new Import.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive),
            MatchRolesByName = true
        });

        Assert.Empty(result.Failures);

        await using var db = NewContext();
        var imported = await db.Files.SingleAsync(Ct);
        Assert.Equal("notes.txt", imported.Name);
        Assert.Equal("exported bytes", await File.ReadAllTextAsync(imported.Path, Ct));
    }

    /// <summary>
    /// Reported per view rather than thrown, so an archive of many views does not lose the ones that
    /// would have succeeded.
    /// </summary>
    [Fact]
    public async Task Import_reports_a_view_that_already_exists_as_a_failure()
    {
        var view = TestData.View("Already here");
        await Seed(view);

        var archive = await SendAsync(new Export.Query { Ids = [view.Id], ArchiveType = ArchiveType.zip });

        var result = await SendAsync(new Import.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive),
            MatchRolesByName = true
        });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(ImportViewFailureType.ViewExists, failure.FailureType);
        Assert.Equal(view.Id, failure.Id);
    }

    [Fact]
    public async Task Import_reports_an_archive_with_no_view_json_as_a_failure()
    {
        var archive = await RootHost.Resolve<Player.Api.Services.IArchiveService>()
            .ArchiveData("empty", ArchiveType.zip, new Dictionary<string, object> { ["readme.txt"] = "nothing here" });

        var result = await SendAsync(new Import.Command { Archive = ArchiveHelper.AsFormFile(archive) });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(ViewConstants.ExportFileName, failure.Name);
    }

    [Fact]
    public async Task Import_is_forbidden_without_ManageViews()
    {
        var archive = await RootHost.Resolve<Player.Api.Services.IArchiveService>()
            .ArchiveData("empty", ArchiveType.zip, new Dictionary<string, object> { ["a.txt"] = "b" });

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Import.Command { Archive = ArchiveHelper.AsFormFile(archive) }));
    }

    public override async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }

        await base.DisposeAsync();
    }
}
