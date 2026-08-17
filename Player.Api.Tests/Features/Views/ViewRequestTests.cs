// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Constants;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// Covers the <c>Views</c> feature over HTTP: the real routes, the real middleware, the real claims
/// transformer, the real handlers, the real AutoMapper profiles, a real database.
/// </summary>
public class ViewRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>
    /// The import route, with both of its flags. Neither is optional: <c>[AsParameters]</c> binds them as
    /// non-nullable value types, so a request omitting either is answered with a bare 400 and an empty
    /// body before the handler is reached.
    /// </summary>
    private const string ImportRoute =
        "api/views/actions/import?matchRolesByName=true&matchApplicationTemplatesByName=true";

    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_view_and_returns_it()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/views",
            new
            {
                name = "Exercise",
                description = "A description",
                status = "Inactive",
                isTemplate = true,
                createAdminTeam = false
            },
            Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<View>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Exercise", created.Name);
        Assert.Equal(ViewStatus.Inactive, created.Status);
        Assert.True(created.IsTemplate);

        // The only assertion in this file on the route name CreatedAtRoute resolves, which is what makes
        // the Location header point at something a client can follow. The host is the test server's, so
        // only the path is the endpoint's own doing.
        Assert.Equal($"/api/views/{created.Id}", response.Headers.Location?.AbsolutePath);

        await using var db = NewContext();
        Assert.Equal("Exercise", (await db.Views.SingleAsync(x => x.Id == created.Id, Ct)).Name);
    }

    [Fact]
    public async Task Create_honors_a_caller_supplied_id()
    {
        var id = Guid.NewGuid();

        var created = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            "api/views", new { id, name = "Fixed", createAdminTeam = false }, Ct));

        Assert.Equal(id, created.Id);
    }

    /// <summary>
    /// An explicitly empty id means "assign one", not "use Guid.Empty" — no serializer lets the request
    /// model tell an absent id from a defaulted one, so the handler treats them alike.
    /// </summary>
    [Fact]
    public async Task Create_assigns_an_id_when_the_caller_sends_an_empty_one()
    {
        var created = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            "api/views",
            new { id = Guid.Empty, name = "Empty id", createAdminTeam = false },
            Ct));

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
        var created = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            "api/views", new { name = "With admins" }, Ct));

        await using var db = NewContext();
        var team = await db.Teams.SingleAsync(x => x.ViewId == created.Id, Ct);
        Assert.Equal("Admin", team.Name);

        var viewMembership = await db.ViewMemberships
            .SingleAsync(x => x.ViewId == created.Id && x.UserId == Root.Id, Ct);
        var teamMembership = await db.TeamMemberships
            .SingleAsync(x => x.TeamId == team.Id && x.UserId == Root.Id, Ct);

        Assert.Equal(viewMembership.Id, teamMembership.ViewMembershipId);
        Assert.Equal(teamMembership.Id, viewMembership.PrimaryTeamMembershipId);
    }

    /// <summary>
    /// <c>Roles:DefaultViewCreatorRole</c> (<c>appsettings.json</c>, <c>View Admin</c>) is resolved with
    /// <c>SingleAsync</c>, so a value matching no team role fails the request with a bare
    /// <c>InvalidOperationException</c> — a 500, not a 400 naming the misconfiguration.
    /// </summary>
    [Fact]
    public async Task Create_fails_when_the_configured_view_creator_role_does_not_exist()
    {
        var role = await Db.TeamRoles.SingleAsync(x => x.Id == TestData.TeamRoles.ViewAdmin, Ct);
        role.Name = $"Renamed {Guid.NewGuid():N}";
        await Db.SaveChangesAsync(Ct);

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync("api/views", new { name = "Doomed" }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
    }

    [Fact]
    public async Task Create_is_forbidden_without_CreateViews()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewViews).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            "api/views", new { name = "Nope", createAdminTeam = false }, Ct));
    }

    // ---- Get ------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_view()
    {
        var view = TestData.View("Findable");
        await Seed(view);

        var got = await ReadAsync<View>(await RootClient.GetAsync($"api/views/{view.Id}", Ct));

        Assert.Equal("Findable", got.Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/views/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// A member of the view may read it without <c>ViewViews</c>: the second clause of
    /// <c>Authorize</c> falls back to the view ids in the caller's team claims, which a membership
    /// granting nothing still produces.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_member_of_the_view_without_ViewViews()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team).SeedAsync();

        var got = await ReadAsync<View>(await Client(actor).GetAsync($"api/views/{view.Id}", Ct));

        Assert.Equal(view.Id, got.Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_member_of_a_different_view()
    {
        var view = TestData.View();
        var elsewhere = TestData.View("Elsewhere");
        var role = TestData.TeamRole();
        var team = TestData.Team(elsewhere.Id, "Team", role.Id);
        await Seed(view, elsewhere, role, team);

        var actor = await Actor().OnTeam(team).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/views/{view.Id}", Ct));
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

        var views = await ReadAsync<View[]>(await RootClient.GetAsync("api/views", Ct));

        Assert.Equal(2, views.Length);
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_ViewViews()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/views", Ct));
    }

    // ---- GetByUser ------------------------------------------------------------------------------

    [Fact]
    public async Task GetByUser_returns_only_the_views_the_user_is_a_member_of()
    {
        var member = TestData.View("Member of");
        var other = TestData.View("Not a member of");
        var user = TestData.User();
        await Seed(member, other, user, TestData.ViewMembership(member.Id, user.Id));

        var views = await ReadAsync<View[]>(await RootClient.GetAsync($"api/users/{user.Id}/views", Ct));

        Assert.Equal("Member of", Assert.Single(views).Name);
    }

    [Fact]
    public async Task GetByUser_reports_a_missing_user_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/users/{Guid.NewGuid()}/views", Ct));
    }

    /// <summary>
    /// Callers may always ask about themselves. The identity check short-circuits before the
    /// permission check, so no permission is needed at all.
    /// </summary>
    [Fact]
    public async Task GetByUser_is_allowed_for_the_caller_asking_about_themselves()
    {
        var actor = await Actor().SeedAsync();

        Assert.Empty(await ReadAsync<View[]>(
            await Client(actor).GetAsync($"api/users/{actor.Id}/views", Ct)));
    }

    [Fact]
    public async Task GetByUser_is_forbidden_for_another_user_without_ViewUsers()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/users/{Guid.NewGuid()}/views", Ct));
    }

    /// <summary>
    /// The one route in this feature that names no user: the caller comes from the <c>sub</c> claim, so
    /// the answer is the same as asking about themselves by id.
    /// </summary>
    [Fact]
    public async Task GetMyViews_returns_the_views_the_caller_is_a_member_of()
    {
        var view = TestData.View("Mine");
        var other = TestData.View("Not mine");
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, other, role, team);

        var actor = await Actor().OnTeam(team).SeedAsync();

        var views = await ReadAsync<View[]>(await Client(actor).GetAsync("api/me/views", Ct));

        Assert.Equal("Mine", Assert.Single(views).Name);
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_updates_the_view()
    {
        var view = TestData.View("Before");
        await Seed(view);

        var edited = await ReadAsync<View>(await RootClient.PutAsJsonAsync(
            $"api/views/{view.Id}",
            new
            {
                name = "After",
                description = "Now described",
                status = "Inactive",
                isTemplate = true
            },
            Ct));

        Assert.Equal("After", edited.Name);
        Assert.Equal(ViewStatus.Inactive, edited.Status);

        await using var db = NewContext();
        Assert.Equal("After", (await db.Views.SingleAsync(x => x.Id == view.Id, Ct)).Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/views/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_view_member_without_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ViewView]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync(
            $"api/views/{view.Id}", new { name = "Nope" }, Ct));
    }

    [Fact]
    public async Task Edit_is_allowed_for_a_view_member_holding_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        var edited = await ReadAsync<View>(await Client(actor).PutAsJsonAsync(
            $"api/views/{view.Id}", new { name = "Allowed" }, Ct));

        Assert.Equal("Allowed", edited.Name);
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_view()
    {
        var view = TestData.View();
        await Seed(view);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/views/{view.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
    }

    /// <summary>
    /// A view membership points at a team membership the cascade would also delete, so the handler
    /// clears that pointer first. Without it the delete fails on the foreign key.
    /// </summary>
    [Fact]
    public async Task Delete_removes_a_view_whose_memberships_have_a_primary_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var member = await Actor().OnTeam(team).SeedAsync();

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/views/{view.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
        Assert.False(await db.TeamMemberships
            .AnyAsync(x => x.Id == member.Membership.TeamMembershipId, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/views/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageViews()
    {
        var view = TestData.View();
        await Seed(view);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/views/{view.Id}", Ct));
    }

    // ---- Clone ----------------------------------------------------------------------------------

    [Fact]
    public async Task Clone_copies_the_view_and_its_teams()
    {
        var view = TestData.View("Original");
        view.Description = "Original description";
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);

        var response = await RootClient.PostAsJsonAsync($"api/views/{view.Id}/clone", new { }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var clone = await ReadAsync<View>(response);

        Assert.NotEqual(view.Id, clone.Id);
        Assert.Equal("Clone of Original", clone.Name);
        Assert.Equal("Original description", clone.Description);
        Assert.Equal(ViewStatus.Active, clone.Status);

        // Clone answers with a Location pointing at the new view, not at the one it copied.
        Assert.Equal($"/api/views/{clone.Id}", response.Headers.Location?.AbsolutePath);

        await using var db = NewContext();
        Assert.Equal("Alpha", (await db.Teams.SingleAsync(x => x.ViewId == clone.Id, Ct)).Name);
    }

    [Fact]
    public async Task Clone_uses_the_supplied_name_and_description_when_given()
    {
        var view = TestData.View("Original");
        view.Description = "Original description";
        await Seed(view);

        var clone = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/clone",
            new { name = "Renamed", description = "Redescribed", isTemplate = true },
            Ct));

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

        var clone = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/clone", new { name = "   " }, Ct));

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

        var clone = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/clone", new { }, Ct));

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

        var clone = await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/clone", new { }, Ct));

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

        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewViews).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            $"api/views/{view.Id}/clone", new { }, Ct));
    }

    // ---- Notifications --------------------------------------------------------------------------

    [Fact]
    public async Task SendNotification_persists_it_and_broadcasts_to_the_view_group()
    {
        var view = TestData.View("Broadcast target");
        await Seed(view);

        var result = await ReadAsync<string>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/notifications",
            new { subject = "Heads up", text = "Something happened" },
            Ct));

        Assert.Contains(view.Id.ToString(), result);

        await using var db = NewContext();
        var notification = await db.Notifications.SingleAsync(Ct);
        Assert.Equal("Something happened", notification.Text);
        Assert.Equal(view.Id, notification.ToId);
        Assert.Equal(NotificationType.View, notification.ToType);
        Assert.Equal(Root.Id, notification.FromId);

        // The recorder is shared by the whole run, so the assertion reads this view's group — a name no
        // other test uses — rather than everything that was broadcast.
        var broadcast = Assert.Single(ViewBroadcasts(view.Id));

        Assert.Equal("Reply", broadcast.Method);
        Assert.Equal("Something happened", Assert.IsType<Notification>(broadcast.Argument).Text);
    }

    /// <summary>
    /// Rejected before anything is persisted, since broadcasting it would push a blank row to every
    /// connected client — but <c>ArgumentException</c> is not an <c>IApiException</c>, so the caller's
    /// own mistake comes back as a 500 rather than a 400.
    /// </summary>
    /// <remarks>Turns red when the handler throws something that maps to a client error.</remarks>
    [Fact]
    public async Task SendNotification_rejects_a_notification_with_no_text()
    {
        var view = TestData.View();
        await Seed(view);

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync(
                $"api/views/{view.Id}/notifications", new { subject = "No body" }, Ct));

        Assert.Equal($"Message was NOT sent to view {view.Id}", problem.Detail);

        await using var db = NewContext();
        Assert.False(await db.Notifications.AnyAsync(Ct));
    }

    /// <summary>
    /// Asserted by text rather than by comparing the two broadcast times to each other: a comparison holds
    /// on a tie whichever order the rows came back in, and it never says which notification is which, so it
    /// would also hold if the same one were returned twice.
    /// </summary>
    [Fact]
    public async Task GetNotifications_returns_the_view_notifications_newest_first()
    {
        var view = TestData.View();
        await Seed(view);

        await Broadcast(view.Id, "First");
        await Broadcast(view.Id, "Second");

        var notifications = await ReadAsync<Notification[]>(
            await RootClient.GetAsync($"api/views/{view.Id}/notifications", Ct));

        Assert.Equal(["Second", "First"], notifications.Select(x => x.Text));
    }

    /// <summary>
    /// The broadcast time reaches a client carrying its UTC designator, so clients do not render it as
    /// local. Neither provider preserves <see cref="DateTimeKind"/>, so the service re-applies it.
    /// </summary>
    [Fact]
    public async Task GetNotifications_returns_broadcast_times_as_UTC()
    {
        var view = TestData.View();
        await Seed(view);
        await Broadcast(view.Id, "Timed");

        var notification = Assert.Single(await ReadAsync<Notification[]>(
            await RootClient.GetAsync($"api/views/{view.Id}/notifications", Ct)));

        Assert.Equal(DateTimeKind.Utc, notification.BroadcastTime.Kind);
    }

    [Fact]
    public async Task DeleteNotification_removes_it_and_tells_the_view_group()
    {
        var view = TestData.View();
        await Seed(view);
        await Broadcast(view.Id, "Doomed");

        var key = Assert.Single(await ReadAsync<Notification[]>(
            await RootClient.GetAsync($"api/views/{view.Id}/notifications", Ct))).Key;

        var result = await ReadAsync<string>(
            await RootClient.DeleteAsync($"api/views/{view.Id}/notifications/{key}", Ct));

        Assert.Contains(key.ToString(), result);

        await using var db = NewContext();
        Assert.False(await db.Notifications.AnyAsync(Ct));

        // The arranging broadcast reached the same group, so the group name alone would not tell the two
        // apart. The message does: only the delete sends "Delete" with the key.
        Assert.Contains(
            ViewBroadcasts(view.Id),
            x => x.Method == "Delete" && Equals(x.Argument, key));
    }

    [Fact]
    public async Task DeleteAllNotifications_removes_every_notification_for_the_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Untouched");
        await Seed(view, otherView);

        await Broadcast(view.Id, "One");
        await Broadcast(view.Id, "Two");
        await Broadcast(otherView.Id, "Other");

        await ReadAsync<string>(await RootClient.DeleteAsync($"api/views/{view.Id}/notifications", Ct));

        await using var db = NewContext();
        Assert.Equal("Other", (await db.Notifications.SingleAsync(Ct)).Text);
    }

    [Fact]
    public async Task GetNotifications_is_forbidden_for_a_non_member_without_ViewViews()
    {
        var view = TestData.View();
        await Seed(view);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/views/{view.Id}/notifications", Ct));
    }

    // ---- Export ---------------------------------------------------------------------------------

    [Fact]
    public async Task Export_produces_an_archive_containing_the_view_json()
    {
        var view = TestData.View("Exported");
        await Seed(view);

        var response = await Export(view.Id, ArchiveType.zip);

        Assert.False(HasArchiveErrors(response));
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.NotEmpty(bytes);

        var name = ArchiveName(response);
        Assert.EndsWith(".zip", name);
        Assert.Contains(ViewConstants.ExportFileName, ArchiveHelper.ExtractFiles(bytes, name).Keys);
    }

    /// <summary>
    /// No <c>ids</c> in the query string means "everything", not "nothing" — the handler only narrows
    /// the query when ids were supplied.
    /// </summary>
    [Fact]
    public async Task Export_with_no_ids_exports_every_view()
    {
        await Seed(TestData.View("One"), TestData.View("Two"));

        var response = await RootClient.GetAsync("api/views/actions/export?archiveType=zip", Ct);
        await AssertStatus(HttpStatusCode.OK, response);

        var views = ArchiveHelper.ReadExportedViews(
            await response.Content.ReadAsByteArrayAsync(Ct), ArchiveName(response));

        Assert.Equal(2, views.Length);
    }

    /// <summary>
    /// The other half of that rule: <c>ids</c> is an array and genuinely optional, but
    /// <c>archiveType</c> is a non-nullable enum, so the bare route the description above describes is
    /// refused by binding with an empty body.
    /// </summary>
    [Fact]
    public async Task Export_without_an_archive_type_is_a_bare_400()
    {
        var actor = await Actor().SeedAsync();

        var response = await Client(actor).GetAsync("api/views/actions/export", Ct);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
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

        var response = await Export(view.Id, ArchiveType.tgz);

        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        var name = ArchiveName(response);
        var json = ArchiveHelper.ExtractFiles(bytes, name)[ViewConstants.ExportFileName];

        Assert.All(json, b => Assert.True(b < 0x80));
        Assert.Contains("\\u00DC", Encoding.UTF8.GetString(json));

        // Escaping is a wire detail, not a data loss — it decodes back to the name that was exported.
        Assert.Equal("Übung", Assert.Single(ArchiveHelper.ReadExportedViews(bytes, name)).Name);
    }

    [Fact]
    public async Task Export_round_trips_through_Import()
    {
        var view = TestData.View("Round trip");
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);

        var exported = await Export(view.Id, ArchiveType.zip);

        // The exported view keeps its id, so it has to be gone before the import will accept it.
        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/views/{view.Id}", Ct));

        var result = await Import(exported);

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

        using var upload = new MultipartFormDataContent
        {
            { new StringContent(view.Id.ToString()), "viewId" },
            { new StringContent(team.Id.ToString()), "teamIds" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("exported bytes")), "ToUpload", "notes.txt" }
        };

        await AssertStatus(
            HttpStatusCode.Created,
            await RootClient.PostAsync("api/files", upload, Ct));

        var exported = await Export(view.Id, ArchiveType.zip);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/views/{view.Id}", Ct));

        var result = await Import(exported);

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

        var result = await Import(await Export(view.Id, ArchiveType.zip));

        var failure = Assert.Single(result.Failures);
        Assert.Equal(ImportViewFailureType.ViewExists, failure.FailureType);
        Assert.Equal(view.Id, failure.Id);
    }

    [Fact]
    public async Task Import_reports_an_archive_with_no_view_json_as_a_failure()
    {
        var archive = await new ArchiveService().ArchiveData(
            "empty",
            ArchiveType.zip,
            new Dictionary<string, object> { ["readme.txt"] = "nothing here" });

        using var upload = ArchiveHelper.AsUpload(Bytes(archive), archive.Name);
        var result = await ReadAsync<Import.ImportViewsResult>(
            await RootClient.PostAsync(ImportRoute, upload, Ct));

        var failure = Assert.Single(result.Failures);
        Assert.Equal(ViewConstants.ExportFileName, failure.Name);
    }

    [Fact]
    public async Task Import_is_forbidden_without_ManageViews()
    {
        var archive = await new ArchiveService().ArchiveData(
            "empty",
            ArchiveType.zip,
            new Dictionary<string, object> { ["a.txt"] = "b" });

        var actor = await Actor().SeedAsync();

        using var upload = ArchiveHelper.AsUpload(Bytes(archive), archive.Name);

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).PostAsync(ImportRoute, upload, Ct));
    }

    /// <summary>
    /// Both match-by-name flags are required, so omitting one is refused by parameter binding — ahead of
    /// authorization, with none of the problem body every other refusal carries.
    /// </summary>
    /// <remarks>Turns red when either flag becomes optional, or the app configures a binding response.</remarks>
    [Theory]
    [InlineData("api/views/actions/import?matchRolesByName=true")]
    [InlineData("api/views/actions/import?matchApplicationTemplatesByName=true")]
    public async Task Import_without_both_match_flags_is_a_bare_400(string route)
    {
        var archive = await new ArchiveService().ArchiveData(
            "empty",
            ArchiveType.zip,
            new Dictionary<string, object> { ["a.txt"] = "b" });

        var actor = await Actor().SeedAsync();

        using var upload = ArchiveHelper.AsUpload(Bytes(archive), archive.Name);

        // An actor holding nothing, so the 400 is binding's: the handler would have answered 403.
        var response = await Client(actor).PostAsync(route, upload, Ct);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>What the notification handlers broadcast to a view's group.</summary>
    private IReadOnlyList<HubBroadcast> ViewBroadcasts(Guid viewId) =>
        Factory.Hub<ViewHub>().ToGroup(viewId);

    /// <summary>Sends a notification to a view as <c>Root</c>, for the tests that read them back.</summary>
    private async Task Broadcast(Guid viewId, string text) =>
        await ReadAsync<string>(await RootClient.PostAsJsonAsync(
            $"api/views/{viewId}/notifications", new { text }, Ct));

    private async Task<HttpResponseMessage> Export(Guid viewId, ArchiveType archiveType)
    {
        var response = await RootClient.GetAsync(
            $"api/views/actions/export?ids={viewId}&archiveType={archiveType}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);

        return response;
    }

    /// <summary>Uploads the archive an export answered with, under the file part the importer binds.</summary>
    private async Task<Import.ImportViewsResult> Import(HttpResponseMessage exported)
    {
        using var upload = ArchiveHelper.AsUpload(
            await exported.Content.ReadAsByteArrayAsync(Ct), ArchiveName(exported));

        return await ReadAsync<Import.ImportViewsResult>(
            await RootClient.PostAsync(ImportRoute, upload, Ct));
    }

    /// <summary>
    /// The archive's file name, which the importer and the extractor read the archive type from.
    /// </summary>
    private static string ArchiveName(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    /// <summary>
    /// Whether the response carries the archive-errors marker. Both collections are checked because the
    /// assertion here is a negative one, and a header looked for in the wrong place is absent from it.
    /// </summary>
    private static bool HasArchiveErrors(HttpResponseMessage response) =>
        response.Headers.Contains(HttpConstants.ArchiveErrorsHeader) ||
        response.Content.Headers.Contains(HttpConstants.ArchiveErrorsHeader);

    /// <summary>An archive built in the test, as a response would have carried it.</summary>
    private static byte[] Bytes(ArchiveResult archive)
    {
        using var stream = new MemoryStream();
        archive.Data.CopyTo(stream);

        return stream.ToArray();
    }
}
