// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Users;
using Player.Api.Hubs;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Features.Users;

/// <summary>
/// Covers the <c>Users</c> feature over HTTP: the real routes, the real middleware, the real claims
/// transformer, the real handlers, the real AutoMapper profiles, a real database.
/// </summary>
public class UserRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_user_and_returns_it()
    {
        var id = Guid.NewGuid();

        var response = await RootClient.PostAsJsonAsync("api/users", new { id, name = "New Person" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<User>(response);

        Assert.Equal(id, created.Id);
        Assert.Equal("New Person", created.Name);

        // The only assertion in this file on the route name CreatedAtRoute resolves, which is what makes
        // the Location header point at something a client can follow. The host is the test server's, so
        // only the path is the endpoint's own doing.
        Assert.Equal($"/api/users/{created.Id}", response.Headers.Location?.AbsolutePath);

        await using var db = NewContext();
        Assert.True(await db.Users.AnyAsync(x => x.Id == created.Id, Ct));
    }

    /// <summary>
    /// The role reaches the response as a name as well as an id, which the projection resolves by
    /// navigation rather than from the request.
    /// </summary>
    [Fact]
    public async Task Create_assigns_the_named_role()
    {
        var role = await Db.Roles.AsNoTracking()
            .SingleAsync(x => x.Id == TestData.Roles.ContentDeveloper, Ct);

        var created = await ReadAsync<User>(await RootClient.PostAsJsonAsync(
            "api/users",
            new { id = Guid.NewGuid(), name = "With a role", roleId = role.Id },
            Ct));

        Assert.Equal(role.Id, created.RoleId);
        Assert.Equal(role.Name, created.RoleName);
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            "api/users", new { id = Guid.NewGuid(), name = "Nope" }, Ct));
    }

    // ---- Get / GetAll ---------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_user()
    {
        var user = TestData.User(name: "Findable");
        await Seed(user);

        var got = await ReadAsync<User>(await RootClient.GetAsync($"api/users/{user.Id}", Ct));

        Assert.Equal("Findable", got.Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_user_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/users/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// Callers may always ask about themselves: the identity check short-circuits before the permission
    /// check, so an actor holding nothing still gets an answer.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_the_caller_asking_about_themselves()
    {
        var actor = await Actor().WithName("Me").SeedAsync();

        var got = await ReadAsync<User>(await Client(actor).GetAsync($"api/users/{actor.Id}", Ct));

        Assert.Equal("Me", got.Name);
    }

    /// <summary>
    /// The fallback branch: a caller with no user-level permission may still read a user they share a
    /// team with, provided they hold <c>ViewTeam</c> on it.
    /// </summary>
    /// <remarks>
    /// Issue 22 is what decides this one in practice — the fallback's empty required-system-permission
    /// array succeeds for every caller, so the <c>ViewTeam</c> grant is not load-bearing. It goes red
    /// when the fallback stops consulting the target user's teams at all.
    /// </remarks>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewTeam_on_a_team_the_user_belongs_to()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var subject = TestData.User(name: "Teammate");
        var viewMembership = TestData.ViewMembership(view.Id, subject.Id);
        await Seed(
            view, role, team, subject, viewMembership,
            TestData.TeamMembership(team.Id, subject.Id, viewMembership.Id));

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var got = await ReadAsync<User>(await Client(actor).GetAsync($"api/users/{subject.Id}", Ct));

        Assert.Equal("Teammate", got.Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_sharing_no_team_with_the_user()
    {
        var user = TestData.User();
        await Seed(user);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/users/{user.Id}", Ct));
    }

    /// <summary>
    /// Unfiltered by design: the permission is global, so every user row is returned — the caller's own
    /// included, since the claims transformer has already written it.
    /// </summary>
    [Fact]
    public async Task GetAll_returns_every_user()
    {
        await Seed(TestData.User(name: "One"), TestData.User(name: "Two"));

        var users = await ReadAsync<User[]>(await RootClient.GetAsync("api/users", Ct));

        Assert.Equal(3, users.Length);
        Assert.Contains(users, x => x.Id == Root.Id);
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_ViewUsers()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/users", Ct));
    }

    // ---- GetByTeam / GetByView ------------------------------------------------------------------

    [Fact]
    public async Task GetByTeam_returns_the_team_members()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other");
        var member = TestData.User(name: "Member");
        var stranger = TestData.User(name: "Stranger");
        var memberView = TestData.ViewMembership(view.Id, member.Id);
        var strangerView = TestData.ViewMembership(view.Id, stranger.Id);
        await Seed(
            view, team, other, member, stranger, memberView, strangerView,
            TestData.TeamMembership(team.Id, member.Id, memberView.Id),
            TestData.TeamMembership(other.Id, stranger.Id, strangerView.Id));

        var users = await ReadAsync<User[]>(await RootClient.GetAsync($"api/teams/{team.Id}/users", Ct));

        Assert.Equal("Member", Assert.Single(users).Name);
    }

    [Fact]
    public async Task GetByTeam_reports_a_missing_team_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/teams/{Guid.NewGuid()}/users", Ct));
    }

    [Fact]
    public async Task GetByView_returns_the_view_members()
    {
        var view = TestData.View();
        var other = TestData.View("Other");
        var member = TestData.User(name: "Member");
        var stranger = TestData.User(name: "Stranger");
        await Seed(
            view, other, member, stranger,
            TestData.ViewMembership(view.Id, member.Id),
            TestData.ViewMembership(other.Id, stranger.Id));

        var users = await ReadAsync<User[]>(await RootClient.GetAsync($"api/views/{view.Id}/users", Ct));

        Assert.Equal("Member", Assert.Single(users).Name);
    }

    [Fact]
    public async Task GetByView_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/views/{Guid.NewGuid()}/users", Ct));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_renames_the_user()
    {
        var user = TestData.User(name: "Before");
        await Seed(user);

        var edited = await ReadAsync<User>(await RootClient.PutAsJsonAsync(
            $"api/users/{user.Id}", new { name = "After" }, Ct));

        Assert.Equal("After", edited.Name);

        await using var db = NewContext();
        Assert.Equal("After", (await db.Users.SingleAsync(x => x.Id == user.Id, Ct)).Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_user_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/users/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    [Fact]
    public async Task Edit_is_forbidden_without_ManageUsers()
    {
        var user = TestData.User();
        await Seed(user);

        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync(
            $"api/users/{user.Id}", new { name = "Nope" }, Ct));
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_user()
    {
        var user = TestData.User();
        await Seed(user);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/users/{user.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Users.AnyAsync(x => x.Id == user.Id, Ct));
    }

    /// <summary>
    /// Refused by identity rather than by permission, and before the lookup: <c>Root</c> holds every
    /// system permission and still cannot delete itself.
    /// </summary>
    [Fact]
    public async Task Delete_refuses_to_delete_the_caller_own_account()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Forbidden,
            await RootClient.DeleteAsync($"api/users/{Root.Id}", Ct));

        Assert.Contains("your own account", problem.Title);

        await using var db = NewContext();
        Assert.True(await db.Users.AnyAsync(x => x.Id == Root.Id, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_user_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/users/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageUsers()
    {
        var user = TestData.User();
        await Seed(user);

        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/users/{user.Id}", Ct));
    }

    // ---- AddToTeam ------------------------------------------------------------------------------

    /// <summary>
    /// A first team in a view brings a view membership with it, and that membership becomes primary —
    /// there is nothing else for it to point at.
    /// </summary>
    [Fact]
    public async Task AddToTeam_creates_the_view_membership_and_makes_it_primary()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        await AssertStatus(HttpStatusCode.OK, await AddToTeam(RootClient, team.Id, user.Id));

        await using var db = NewContext();
        var viewMembership = await db.ViewMemberships.SingleAsync(x => x.UserId == user.Id, Ct);
        var teamMembership = await db.TeamMemberships.SingleAsync(x => x.UserId == user.Id, Ct);
        Assert.Equal(view.Id, viewMembership.ViewId);
        Assert.Equal(team.Id, teamMembership.TeamId);
        Assert.Equal(teamMembership.Id, viewMembership.PrimaryTeamMembershipId);
    }

    /// <summary>
    /// A second team in the same view reuses the existing view membership and leaves the primary alone.
    /// </summary>
    [Fact]
    public async Task AddToTeam_reuses_an_existing_view_membership()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var firstMembership = TestData.TeamMembership(first.Id, user.Id, viewMembership.Id);
        await Seed(view, first, second, user, viewMembership, firstMembership);
        viewMembership.PrimaryTeamMembershipId = firstMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await AssertStatus(HttpStatusCode.OK, await AddToTeam(RootClient, second.Id, user.Id));

        await using var db = NewContext();
        Assert.Single(await db.ViewMemberships.Where(x => x.UserId == user.Id).ToListAsync(Ct));
        Assert.Equal(2, await db.TeamMemberships.CountAsync(x => x.UserId == user.Id, Ct));
        Assert.Equal(
            firstMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_team_as_not_found()
    {
        var user = TestData.User();
        await Seed(user);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await AddToTeam(RootClient, Guid.NewGuid(), user.Id));
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await AddToTeam(RootClient, team.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task AddToTeam_is_forbidden_without_ManageTeam()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var user = TestData.User();
        await Seed(view, role, team, user);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await AddToTeam(Client(actor), team.Id, user.Id));
    }

    // ---- RemoveFromTeam -------------------------------------------------------------------------

    /// <summary>
    /// Removing the user's only team in a view removes the view membership too, since a view membership
    /// with no team membership is not a state the model allows.
    /// </summary>
    [Fact]
    public async Task RemoveFromTeam_removes_the_view_membership_along_with_the_last_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);
        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.DeleteAsync($"api/teams/{team.Id}/users/{user.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamMemberships.AnyAsync(x => x.UserId == user.Id, Ct));
        Assert.False(await db.ViewMemberships.AnyAsync(x => x.UserId == user.Id, Ct));
    }

    /// <summary>
    /// Removing the primary team moves the primary to another of the user's teams in that view, rather
    /// than leaving the membership pointing at a deleted row.
    /// </summary>
    [Fact]
    public async Task RemoveFromTeam_moves_the_primary_when_it_is_the_team_being_removed()
    {
        var view = TestData.View();
        var primary = TestData.Team(view.Id, "Primary");
        var secondary = TestData.Team(view.Id, "Secondary");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var primaryMembership = TestData.TeamMembership(primary.Id, user.Id, viewMembership.Id);
        var secondaryMembership = TestData.TeamMembership(secondary.Id, user.Id, viewMembership.Id);
        await Seed(view, primary, secondary, user, viewMembership, primaryMembership, secondaryMembership);
        viewMembership.PrimaryTeamMembershipId = primaryMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.DeleteAsync($"api/teams/{primary.Id}/users/{user.Id}", Ct));

        await using var db = NewContext();
        Assert.Equal(
            secondaryMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
        Assert.Equal(
            secondary.Id,
            (await db.TeamMemberships.SingleAsync(x => x.UserId == user.Id, Ct)).TeamId);
    }

    [Fact]
    public async Task RemoveFromTeam_leaves_the_primary_alone_when_removing_another_team()
    {
        var view = TestData.View();
        var primary = TestData.Team(view.Id, "Primary");
        var secondary = TestData.Team(view.Id, "Secondary");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var primaryMembership = TestData.TeamMembership(primary.Id, user.Id, viewMembership.Id);
        await Seed(
            view, primary, secondary, user, viewMembership, primaryMembership,
            TestData.TeamMembership(secondary.Id, user.Id, viewMembership.Id));
        viewMembership.PrimaryTeamMembershipId = primaryMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.DeleteAsync($"api/teams/{secondary.Id}/users/{user.Id}", Ct));

        await using var db = NewContext();
        Assert.Equal(
            primaryMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
    }

    /// <summary>
    /// A user who is not on the team is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task RemoveFromTeam_does_nothing_when_the_user_is_not_a_member()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.DeleteAsync($"api/teams/{team.Id}/users/{user.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamMemberships.AnyAsync(Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_team_as_not_found()
    {
        var user = TestData.User();
        await Seed(user);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/teams/{Guid.NewGuid()}/users/{user.Id}", Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/teams/{team.Id}/users/{Guid.NewGuid()}", Ct));
    }

    // ---- SendNotification -----------------------------------------------------------------------

    [Fact]
    public async Task SendNotification_persists_it_and_broadcasts_to_the_user_group()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        var result = await ReadAsync<string>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/users/{user.Id}/notifications",
            new { subject = "For you", text = "Something happened" },
            Ct));

        Assert.Contains(user.Id.ToString(), result);

        await using var db = NewContext();
        var notification = await db.Notifications.SingleAsync(Ct);
        Assert.Equal("Something happened", notification.Text);
        Assert.Equal(user.Id, notification.ToId);
        Assert.Equal(NotificationType.User, notification.ToType);
        Assert.Equal(Root.Id, notification.FromId);

        // The recorder is shared by the whole run, so the assertion reads this pair's group — a name no
        // other test uses — rather than everything that was broadcast.
        var broadcast = Assert.Single(UserBroadcasts(view.Id, user.Id));

        Assert.Equal("Reply", broadcast.Method);
        Assert.Equal("Something happened", Assert.IsType<Notification>(broadcast.Argument).Text);
    }

    /// <summary>
    /// Rejected before anything is persisted, which is right — but <c>ArgumentException</c> is not an
    /// <c>IApiException</c>, so the caller's own mistake is answered with a 500. Issue 50: wrong.
    /// </summary>
    /// <remarks>Turns red when the handler throws something that maps to a client error.</remarks>
    [Fact]
    public async Task SendNotification_rejects_a_notification_with_no_text()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync(
                $"api/views/{view.Id}/users/{user.Id}/notifications", new { subject = "No body" }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Equal($"Message was NOT sent to user {user.Id} in view {view.Id}", problem.Detail);

        await using var db = NewContext();
        Assert.False(await db.Notifications.AnyAsync(Ct));
    }

    /// <summary>
    /// This handler authorizes against <c>UserEntity</c>, which
    /// <c>AuthorizationService.GetResourceResult</c> does not handle, so every caller without
    /// <c>ManageViews</c> gets a 500 rather than a decision. Issue 19: wrong — the view administrators
    /// the call's <c>ManageView</c> argument was written for are refused along with everyone else.
    /// </summary>
    /// <remarks>
    /// Turns red when <c>GetResourceResult</c> learns <c>UserEntity</c>, or the call passes a type it
    /// already knows. The <c>detail</c> is asserted because it is what proves the 500 came from the
    /// unhandled type and not from somewhere else in the request.
    /// </remarks>
    [Fact]
    public async Task SendNotification_is_a_server_error_for_a_caller_without_ManageViews()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var user = TestData.User();
        await Seed(view, role, team, user, TestData.ViewMembership(view.Id, user.Id));

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await Client(actor).PostAsJsonAsync(
                $"api/views/{view.Id}/users/{user.Id}/notifications", new { text = "Nope" }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Equal("Handler for type UserEntity is not implemented.", problem.Detail);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// What the user notification handler broadcast to a user's group, which names the view as well —
    /// the same user in two views is two audiences.
    /// </summary>
    private IReadOnlyList<HubBroadcast> UserBroadcasts(Guid viewId, Guid userId) =>
        Factory.Hub<UserHub>().ToGroup($"{viewId}_{userId}");

    /// <summary>
    /// <c>POST teams/{teamId}/users/{userId}</c>, which carries no body — both members of its command
    /// come from the route, and the endpoint binds nothing else.
    /// </summary>
    private static Task<HttpResponseMessage> AddToTeam(HttpClient client, Guid teamId, Guid userId) =>
        client.PostAsync($"api/teams/{teamId}/users/{userId}", content: null, Ct);
}
