// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Hubs;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Features.Teams;

/// <summary>
/// Covers the <c>Teams</c> feature over HTTP: the real routes, the real middleware, the real claims
/// transformer, the real handlers, the real AutoMapper profiles, a real database.
/// </summary>
public class TeamRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_team_in_the_named_view()
    {
        var view = TestData.View();
        await Seed(view);

        var response = await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/teams", new { name = "Blue" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<Team>(response);

        Assert.Equal("Blue", created.Name);
        Assert.Equal(view.Id, created.ViewId);

        // The only assertion in this file on the route name CreatedAtRoute resolves. It is "getTeam",
        // so the Location points at the new team rather than at the view it was created in.
        Assert.Equal($"/api/teams/{created.Id}", response.Headers.Location?.AbsolutePath);

        await using var db = NewContext();
        Assert.Equal("Blue", (await db.Teams.SingleAsync(x => x.Id == created.Id, Ct)).Name);
    }

    /// <summary>
    /// <see cref="TeamEntity.RoleId"/> is required, so an unnamed role falls back to
    /// <c>Roles:DefaultTeamRole</c>.
    /// </summary>
    /// <remarks>
    /// One host serves the whole run, so that option is <c>appsettings.json</c>'s <c>View Member</c> and
    /// no test can vary it. Goes red if the shipped default changes.
    /// </remarks>
    [Fact]
    public async Task Create_falls_back_to_the_configured_default_role()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await ReadAsync<Team>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/teams", new { name = "Defaulted" }, Ct));

        Assert.Equal("View Member", created.RoleName);
    }

    [Fact]
    public async Task Create_uses_the_role_the_caller_named()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await ReadAsync<Team>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/teams",
            new { name = "Admins", roleId = TestData.TeamRoles.ViewAdmin },
            Ct));

        Assert.Equal(TestData.TeamRoles.ViewAdmin, created.RoleId);
    }

    [Fact]
    public async Task Create_honors_a_caller_supplied_id()
    {
        var view = TestData.View();
        await Seed(view);
        var id = Guid.NewGuid();

        var created = await ReadAsync<Team>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/teams", new { id, name = "Fixed" }, Ct));

        Assert.Equal(id, created.Id);
    }

    /// <summary>
    /// An explicitly empty id means "assign one", not "use Guid.Empty" — no serializer lets the request
    /// model tell an absent id from a defaulted one, so the handler treats them alike.
    /// </summary>
    [Fact]
    public async Task Create_assigns_an_id_when_the_caller_sends_an_empty_one()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await ReadAsync<Team>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/teams", new { id = Guid.Empty, name = "Empty id" }, Ct));

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task Create_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.PostAsJsonAsync(
                $"api/views/{Guid.NewGuid()}/teams", new { name = "Orphan" }, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_view_member_without_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ViewView]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            $"api/views/{view.Id}/teams", new { name = "Nope" }, Ct));
    }

    // ---- Get / GetAll / GetByView ---------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_team_with_its_role_name()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Readable", TestData.TeamRoles.Observer);
        await Seed(view, team);

        var got = await ReadAsync<Team>(await RootClient.GetAsync($"api/teams/{team.Id}", Ct));

        Assert.Equal("Readable", got.Name);
        Assert.Equal("Observer", got.RoleName);
    }

    /// <summary>
    /// Authorization resolves the team to its view first, so a missing team reads as forbidden to
    /// everyone but a system-permission holder.
    /// </summary>
    [Fact]
    public async Task Get_reports_a_missing_team_as_not_found_for_a_system_permission_holder()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/teams/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permission_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/teams/{team.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewTeam_on_that_team()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var got = await ReadAsync<Team>(await Client(actor).GetAsync($"api/teams/{team.Id}", Ct));

        Assert.Equal(team.Id, got.Id);
    }

    [Fact]
    public async Task GetAll_returns_every_team_across_every_view()
    {
        var first = TestData.View("First");
        var second = TestData.View("Second");
        await Seed(first, second, TestData.Team(first.Id, "A"), TestData.Team(second.Id, "B"));

        var teams = await ReadAsync<Team[]>(await RootClient.GetAsync("api/teams", Ct));

        Assert.Equal(2, teams.Length);
    }

    [Fact]
    public async Task GetByView_returns_only_that_view_teams()
    {
        var view = TestData.View();
        var other = TestData.View("Other");
        await Seed(view, other, TestData.Team(view.Id, "Mine"), TestData.Team(other.Id, "Theirs"));

        var teams = await ReadAsync<Team[]>(await RootClient.GetAsync($"api/views/{view.Id}/teams", Ct));

        Assert.Equal("Mine", Assert.Single(teams).Name);
    }

    [Fact]
    public async Task GetByView_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/views/{Guid.NewGuid()}/teams", Ct));
    }

    // ---- GetByUserView --------------------------------------------------------------------------

    /// <summary>
    /// The privileged branch: an administrator asking about someone else sees every team in the view,
    /// not only the ones that user belongs to.
    /// </summary>
    /// <remarks>
    /// Two of the handler's three branches are covered because only two are reachable. Its
    /// <c>Authorize</c> admits exactly the callers those two match, so the third answers 403 before
    /// <c>HandleRequest</c> runs.
    /// </remarks>
    [Fact]
    public async Task GetByUserView_returns_every_team_in_the_view_for_a_privileged_caller()
    {
        var view = TestData.View();
        var member = TestData.Team(view.Id, "Member of");
        var notMember = TestData.Team(view.Id, "Not a member of");
        await Seed(view, member, notMember);

        var subject = await Actor().WithName("Subject").OnTeam(member).SeedAsync();

        var teams = await ReadAsync<Team[]>(
            await RootClient.GetAsync($"api/users/{subject.Id}/views/{view.Id}/teams", Ct));

        Assert.Equal(2, teams.Length);
    }

    /// <summary>
    /// The self branch: a caller asking about themselves sees what their primary team's permissions
    /// make visible, which is the scoped-permission rule rather than plain membership.
    /// </summary>
    /// <remarks>
    /// The scope row is what puts <c>Own</c> in the <c>Scoped onto</c> claim's source teams, and the
    /// seeded <c>View Member</c> role the teams default to grants <c>ViewTeam</c> but not
    /// <c>ViewView</c> — with the latter the caller would see all three teams instead.
    /// </remarks>
    [Fact]
    public async Task GetByUserView_uses_the_primary_visibility_context_for_the_caller_themselves()
    {
        var view = TestData.View();
        var own = TestData.Team(view.Id, "Own");
        var scoped = TestData.Team(view.Id, "Scoped onto");
        var invisible = TestData.Team(view.Id, "Invisible");
        await Seed(view, own, scoped, invisible, TestData.TeamPermissionScope(own.Id, scoped.Id));

        var actor = await Actor().OnTeam(own, primary: true).SeedAsync();

        var teams = await ReadAsync<Team[]>(
            await Client(actor).GetAsync($"api/users/{actor.Id}/views/{view.Id}/teams", Ct));

        Assert.Equal(["Own", "Scoped onto"], teams.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetByUserView_reports_a_missing_view_as_not_found()
    {
        var user = TestData.User();
        await Seed(user);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/users/{user.Id}/views/{Guid.NewGuid()}/teams", Ct));
    }

    [Fact]
    public async Task GetByUserView_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        await Seed(view);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/users/{Guid.NewGuid()}/views/{view.Id}/teams", Ct));
    }

    [Fact]
    public async Task GetByUserView_is_forbidden_for_another_user_without_view_access()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/users/{user.Id}/views/{view.Id}/teams", Ct));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_renames_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Before");
        await Seed(view, team);

        var edited = await ReadAsync<Team>(await RootClient.PutAsJsonAsync(
            $"api/teams/{team.Id}", new { name = "After" }, Ct));

        Assert.Equal("After", edited.Name);

        await using var db = NewContext();
        Assert.Equal("After", (await db.Teams.SingleAsync(x => x.Id == team.Id, Ct)).Name);
    }

    [Fact]
    public async Task Edit_changes_the_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.ViewMember);
        await Seed(view, team);

        var edited = await ReadAsync<Team>(await RootClient.PutAsJsonAsync(
            $"api/teams/{team.Id}",
            new { name = team.Name, roleId = TestData.TeamRoles.ViewAdmin },
            Ct));

        Assert.Equal(TestData.TeamRoles.ViewAdmin, edited.RoleId);
        Assert.Equal("View Admin", edited.RoleName);
    }

    /// <summary>
    /// The global null-source convention at work: a null <c>RoleId</c> leaves the existing role rather
    /// than clearing a required column.
    /// </summary>
    [Fact]
    public async Task Edit_leaves_the_role_alone_when_the_caller_omits_it()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.Observer);
        await Seed(view, team);

        var edited = await ReadAsync<Team>(await RootClient.PutAsJsonAsync(
            $"api/teams/{team.Id}", new { name = "Renamed only" }, Ct));

        Assert.Equal(TestData.TeamRoles.Observer, edited.RoleId);
    }

    [Fact]
    public async Task Edit_reports_a_missing_team_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/teams/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/teams/{team.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Teams.AnyAsync(x => x.Id == team.Id, Ct));
    }

    /// <summary>
    /// Both directions are removed explicitly. A scope naming the deleted team as its target has no
    /// cascade path from it, so it would otherwise be stranded.
    /// </summary>
    [Fact]
    public async Task Delete_removes_scopes_pointing_in_either_direction()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Doomed");
        var other = TestData.Team(view.Id, "Survivor");
        await Seed(
            view, team, other,
            TestData.TeamPermissionScope(team.Id, other.Id),
            TestData.TeamPermissionScope(other.Id, team.Id));

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/teams/{team.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionScopes.AnyAsync(Ct));
        Assert.True(await db.Teams.AnyAsync(x => x.Id == other.Id, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_team_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/teams/{Guid.NewGuid()}", Ct));
    }

    // ---- SetPrimary -----------------------------------------------------------------------------

    [Fact]
    public async Task SetPrimary_points_the_view_membership_at_the_named_team()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        await Seed(view, first, second);

        var actor = await Actor().OnTeam(first, primary: true).OnTeam(second).SeedAsync();

        var result = await ReadAsync<Team>(await Client(actor).PostAsync(
            $"api/users/{actor.Id}/teams/{second.Id}/primary", null, Ct));

        Assert.Equal(second.Id, result.Id);

        await using var db = NewContext();
        Assert.Equal(
            actor.On(second.Id).TeamMembershipId,
            (await db.ViewMemberships
                .SingleAsync(x => x.Id == actor.Membership.ViewMembershipId, Ct))
                .PrimaryTeamMembershipId);
    }

    /// <summary>
    /// A conflict, not a forbidden: the caller may make the change, but the target is not a team they
    /// belong to.
    /// </summary>
    [Fact]
    public async Task SetPrimary_rejects_a_team_the_user_is_not_a_member_of()
    {
        var view = TestData.View();
        var member = TestData.Team(view.Id, "Member of");
        var stranger = TestData.Team(view.Id, "Not a member of");
        await Seed(view, member, stranger);

        var actor = await Actor().OnTeam(member).SeedAsync();

        await AssertProblem(HttpStatusCode.Conflict, await Client(actor).PostAsync(
            $"api/users/{actor.Id}/teams/{stranger.Id}/primary", null, Ct));
    }

    /// <summary>
    /// This is wrong: the 409 above is narrower than it looks. It is reached only because that stranger
    /// team sits in the caller's own view; a team in a view the caller has no membership in is a server
    /// error instead.
    /// </summary>
    /// <remarks>
    /// <c>SetPrimary.cs:75-79</c> looks the view membership up with <c>SingleOrDefaultAsync</c> and
    /// <c>:81</c> dereferences it unguarded, so the miss is a <c>NullReferenceException</c> rather than
    /// the <c>ConflictException</c> two lines further on. Turns red when line 81 is guarded. The route
    /// names the caller's own id here and in the test below because <c>Authorize</c>
    /// (<c>SetPrimary.cs:61-67</c>) admits no other subject.
    /// </remarks>
    [Fact]
    public async Task SetPrimary_fails_when_the_team_is_in_a_view_the_caller_is_not_in()
    {
        var view = TestData.View();
        var elsewhere = TestData.View("Elsewhere");
        var home = TestData.Team(view.Id, "Home");
        var away = TestData.Team(elsewhere.Id, "Away");
        await Seed(view, elsewhere, home, away);

        var actor = await Actor().OnTeam(home).SeedAsync();

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await Client(actor).PostAsync($"api/users/{actor.Id}/teams/{away.Id}/primary", null, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Equal("Object reference not set to an instance of an object.", problem.Detail);
    }

    /// <summary>
    /// This is wrong: a team id naming nothing should be a 404, and is a server error instead.
    /// </summary>
    /// <remarks>
    /// <c>SetPrimary.cs:73</c> looks the team up with <c>SingleOrDefaultAsync</c> and never checks the
    /// result; <c>:78</c> then reads <c>teamEntity.ViewId</c> inside the next query's predicate, so EF
    /// reports the dereference as a parameter-evaluation failure. The detail is matched on the fragment
    /// naming that failure rather than in full, because the rest of the sentence is EF's own advice and
    /// changes with the provider. Turns red when the null is checked.
    /// </remarks>
    [Fact]
    public async Task SetPrimary_fails_when_the_team_does_not_exist()
    {
        var actor = await Actor().SeedAsync();

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await Client(actor).PostAsync(
                $"api/users/{actor.Id}/teams/{Guid.NewGuid()}/primary", null, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Contains("attempting to evaluate a LINQ query parameter expression", problem.Detail);
    }

    /// <summary>
    /// This handler's <c>Authorize</c> asks only whether the subject is the caller, so no permission
    /// grants it.
    /// </summary>
    [Fact]
    public async Task SetPrimary_is_forbidden_for_another_user_even_with_every_system_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        await AssertProblem(HttpStatusCode.Forbidden, await RootClient.PostAsync(
            $"api/users/{user.Id}/teams/{team.Id}/primary", null, Ct));
    }

    // ---- SendNotification -----------------------------------------------------------------------

    [Fact]
    public async Task SendNotification_persists_it_and_broadcasts_to_the_team_group()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Recipients");
        await Seed(view, team);

        var result = await ReadAsync<string>(await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/notifications",
            new { subject = "Heads up", text = "Something happened" },
            Ct));

        Assert.Contains(team.Id.ToString(), result);

        await using var db = NewContext();
        var notification = await db.Notifications.SingleAsync(Ct);
        Assert.Equal(team.Id, notification.ToId);
        Assert.Equal(NotificationType.Team, notification.ToType);
        Assert.Equal(Root.Id, notification.FromId);

        // The recorder is shared by the whole run, so the assertion reads this team's group — a name no
        // other test uses — rather than everything that was broadcast.
        var broadcast = Assert.Single(TeamBroadcasts(team.Id));

        Assert.Equal("Reply", broadcast.Method);
        Assert.Equal(team.Id, Assert.IsType<Notification>(broadcast.Argument).ToId);
    }

    /// <summary>
    /// This is wrong: a caller who sends no text is making a client's mistake and is answered with a
    /// server error. <c>ArgumentException</c> is not an <c>IApiException</c>, so
    /// <c>ExceptionMiddleware</c> has no mapping for it and falls through to 500.
    /// </summary>
    /// <remarks>
    /// Issue 50. Turns red when the handler throws something that maps to a client error — nothing else
    /// about the endpoint has to change.
    /// </remarks>
    [Fact]
    public async Task SendNotification_rejects_a_notification_with_no_text()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync(
                $"api/teams/{team.Id}/notifications", new { subject = "No body" }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Equal($"Message was NOT sent to team {team.Id}", problem.Detail);

        await using var db = NewContext();
        Assert.False(await db.Notifications.AnyAsync(Ct));
    }

    [Fact]
    public async Task SendNotification_is_forbidden_without_ManageView_on_the_view()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ManageTeam]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            $"api/teams/{team.Id}/notifications", new { text = "Nope" }, Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>What the team notification handler broadcast to a team's group.</summary>
    private IReadOnlyList<HubBroadcast> TeamBroadcasts(Guid teamId) =>
        Factory.Hub<TeamHub>().ToGroup(teamId);
}
