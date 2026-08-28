// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamMemberships;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamMemberships;

/// <summary>
/// Covers the <c>TeamMemberships</c> feature over HTTP. A team membership carries the role a user holds
/// on one team, so these routes are how a user's team role is read and changed.
/// </summary>
public class TeamMembershipRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Get ------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_membership_with_its_names_resolved()
    {
        var view = TestData.View("Exercise");
        var team = TestData.Team(view.Id, "Blue");
        await Seed(view, team);

        var member = await Actor()
            .WithName("Member")
            .OnTeam(team, roleId: TestData.TeamRoles.Observer)
            .SeedAsync();

        var got = await ReadAsync<TeamMembership>(await RootClient.GetAsync(
            $"api/team-memberships/{member.Membership.TeamMembershipId}", Ct));

        Assert.Equal("Member", got.UserName);
        Assert.Equal("Blue", got.TeamName);
        Assert.Equal("Observer", got.RoleName);
        Assert.Equal(view.Id, got.ViewId);
    }

    /// <summary>
    /// A membership with no role of its own falls back to the team's role, which the projection reports
    /// as no role name rather than the team's.
    /// </summary>
    [Fact]
    public async Task Get_reports_no_role_name_when_the_membership_has_no_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var member = await Actor().OnTeam(team).SeedAsync();

        var got = await ReadAsync<TeamMembership>(await RootClient.GetAsync(
            $"api/team-memberships/{member.Membership.TeamMembershipId}", Ct));

        Assert.Null(got.RoleName);
    }

    /// <summary>
    /// Primacy belongs to the view membership rather than to the team membership, so the projection
    /// resolves it by comparing ids — the second team is what shows the comparison happens.
    /// </summary>
    [Fact]
    public async Task Get_reports_the_primary_membership_as_primary()
    {
        var view = TestData.View();
        var primaryTeam = TestData.Team(view.Id, "Primary");
        var secondTeam = TestData.Team(view.Id, "Second");
        await Seed(view, primaryTeam, secondTeam);

        var member = await Actor().OnTeam(primaryTeam, primary: true).OnTeam(secondTeam).SeedAsync();

        var primary = await ReadAsync<TeamMembership>(await RootClient.GetAsync(
            $"api/team-memberships/{member.On(primaryTeam.Id).TeamMembershipId}", Ct));
        var second = await ReadAsync<TeamMembership>(await RootClient.GetAsync(
            $"api/team-memberships/{member.On(secondTeam.Id).TeamMembershipId}", Ct));

        Assert.True(primary.isPrimary);
        Assert.False(second.isPrimary);
    }

    [Fact]
    public async Task Get_reports_a_missing_membership_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/team-memberships/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// Authorized against the membership's own team, so <c>ManageTeam</c> on that team is enough — and
    /// it is enough for another member's membership, not only the caller's own.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ManageTeam_on_the_team()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var member = await Actor().WithName("Member").OnTeam(team).SeedAsync();
        var caller = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ManageTeam]).SeedAsync();

        var got = await ReadAsync<TeamMembership>(await Client(caller).GetAsync(
            $"api/team-memberships/{member.Membership.TeamMembershipId}", Ct));

        Assert.Equal(member.Membership.TeamMembershipId, got.Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var member = await Actor().WithName("Member").OnTeam(team).SeedAsync();
        var caller = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(caller).GetAsync(
                $"api/team-memberships/{member.Membership.TeamMembershipId}", Ct));
    }

    // ---- GetByUserView --------------------------------------------------------------------------

    [Fact]
    public async Task GetByUserView_returns_only_that_user_memberships_in_that_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id, "In view");
        var otherTeam = TestData.Team(otherView.Id, "Elsewhere");
        await Seed(view, otherView, team, otherTeam);

        var user = await Actor().OnTeam(team).OnTeam(otherTeam).SeedAsync();
        await Actor().WithName("Stranger").OnTeam(team).SeedAsync();

        var memberships = await ReadAsync<TeamMembership[]>(await RootClient.GetAsync(
            $"api/users/{user.Id}/views/{view.Id}/team-memberships", Ct));

        Assert.Equal("In view", Assert.Single(memberships).TeamName);
    }

    /// <summary>
    /// A user may always read their own memberships, which is how the UI resolves the caller's own
    /// teams. The team's role grants nothing, so only the identity check can be what allows this.
    /// </summary>
    [Fact]
    public async Task GetByUserView_is_allowed_for_the_caller_asking_about_themselves()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team).SeedAsync();

        Assert.Single(await ReadAsync<TeamMembership[]>(await Client(actor).GetAsync(
            $"api/users/{actor.Id}/views/{view.Id}/team-memberships", Ct)));
    }

    [Fact]
    public async Task GetByUserView_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        await Seed(view);

        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync(
                $"api/users/{Guid.NewGuid()}/views/{view.Id}/team-memberships", Ct));
    }

    [Fact]
    public async Task GetByUserView_is_forbidden_for_a_caller_asking_about_someone_else()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        var caller = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(caller).GetAsync(
                $"api/users/{user.Id}/views/{view.Id}/team-memberships", Ct));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_changes_the_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var member = await Actor().OnTeam(team, roleId: TestData.TeamRoles.Observer).SeedAsync();

        var edited = await ReadAsync<TeamMembership>(await RootClient.PutAsJsonAsync(
            $"api/team-memberships/{member.Membership.TeamMembershipId}",
            new { roleId = TestData.TeamRoles.ViewAdmin },
            Ct));

        Assert.Equal(TestData.TeamRoles.ViewAdmin, edited.RoleId);
        Assert.Equal("View Admin", edited.RoleName);

        await using var db = NewContext();
        var stored = await db.TeamMemberships
            .SingleAsync(x => x.Id == member.Membership.TeamMembershipId, Ct);

        Assert.Equal(TestData.TeamRoles.ViewAdmin, stored.RoleId);
    }

    /// <summary>
    /// Clearing the role returns the member to the team's own role.
    /// </summary>
    [Fact]
    public async Task Edit_clears_the_role_when_none_is_given()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var member = await Actor().OnTeam(team, roleId: TestData.TeamRoles.Observer).SeedAsync();

        var edited = await ReadAsync<TeamMembership>(await RootClient.PutAsJsonAsync(
            $"api/team-memberships/{member.Membership.TeamMembershipId}",
            new { roleId = (Guid?)null },
            Ct));

        Assert.Null(edited.RoleId);

        await using var db = NewContext();
        var stored = await db.TeamMemberships
            .SingleAsync(x => x.Id == member.Membership.TeamMembershipId, Ct);

        Assert.Null(stored.RoleId);
    }

    [Fact]
    public async Task Edit_reports_a_missing_membership_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/team-memberships/{Guid.NewGuid()}", new { }, Ct));
    }

    /// <summary>
    /// A role id naming no team role is the caller's mistake, but nothing checks it before the save, so
    /// the foreign key fails and the answer is a server error rather than a 400. This is wrong.
    /// </summary>
    /// <remarks>Turns red when the handler validates the role, or maps the failure to a client error.</remarks>
    [Fact]
    public async Task Edit_answers_a_role_that_does_not_exist_with_a_server_error()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var member = await Actor().OnTeam(team, roleId: TestData.TeamRoles.Observer).SeedAsync();

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PutAsJsonAsync(
                $"api/team-memberships/{member.Membership.TeamMembershipId}",
                new { roleId = Guid.NewGuid() },
                Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Contains("saving the entity changes", problem.Detail);

        // The role the member already held is what they still hold: the failed save changed nothing.
        await using var db = NewContext();
        var stored = await db.TeamMemberships
            .SingleAsync(x => x.Id == member.Membership.TeamMembershipId, Ct);

        Assert.Equal(TestData.TeamRoles.Observer, stored.RoleId);
    }

    /// <summary>
    /// Team roles are a view-level concern: <c>ManageTeam</c> on the team is not enough, only
    /// <c>ManageView</c> or the system permission.
    /// </summary>
    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ManageTeam()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var member = await Actor().WithName("Member").OnTeam(team).SeedAsync();
        var caller = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ManageTeam]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(caller).PutAsJsonAsync(
            $"api/team-memberships/{member.Membership.TeamMembershipId}",
            new { roleId = TestData.TeamRoles.ViewAdmin },
            Ct));
    }
}
