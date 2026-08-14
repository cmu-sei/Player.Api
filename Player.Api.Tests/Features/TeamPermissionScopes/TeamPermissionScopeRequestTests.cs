// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamPermissionScopes;

/// <summary>
/// Covers the <c>TeamPermissionScopes</c> feature over HTTP: the real routes, the real middleware, the
/// real claims transformer, the real handlers, a real database. A scope projects one team's permissions
/// onto another, which is how a user on the granting team acts on the target team's resources.
/// </summary>
public class TeamPermissionScopeRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Add ------------------------------------------------------------------------------------

    /// <summary>
    /// Both routes answer <c>200</c> with an empty body: the commands return nothing, so there is no
    /// scope resource for a client to read back.
    /// </summary>
    [Fact]
    public async Task Add_records_the_scope()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        var response = await RootClient.PostAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionScopes.AnyAsync(
            x => x.TeamId == team.Id && x.TargetTeamId == target.Id, Ct));
    }

    /// <summary>
    /// Adding a scope that already exists is not an error, and does not duplicate the row.
    /// </summary>
    [Fact]
    public async Task Add_is_idempotent()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", null, Ct));

        await using var db = NewContext();
        Assert.Single(await db.TeamPermissionScopes.ToListAsync(Ct));
    }

    /// <summary>
    /// A self-scope would be a no-op that reads as a grant, so it is refused rather than stored.
    /// </summary>
    [Fact]
    public async Task Add_refuses_to_scope_a_team_onto_itself()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var problem = await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsync(
            $"api/teams/{team.Id}/scopes/{team.Id}", null, Ct));

        Assert.Equal("A Team cannot scope its permissions onto itself.", problem.Title);
    }

    /// <summary>
    /// Both directions are checked, and the messages distinguish them — a caller needs to know which id
    /// was wrong.
    /// </summary>
    [Fact]
    public async Task Add_reports_a_missing_granting_team_as_not_found()
    {
        var view = TestData.View();
        var target = TestData.Team(view.Id);
        await Seed(view, target);

        var problem = await AssertProblem(HttpStatusCode.NotFound, await RootClient.PostAsync(
            $"api/teams/{Guid.NewGuid()}/scopes/{target.Id}", null, Ct));

        Assert.Contains("Granting", problem.Title);
    }

    [Fact]
    public async Task Add_reports_a_missing_target_team_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var problem = await AssertProblem(HttpStatusCode.NotFound, await RootClient.PostAsync(
            $"api/teams/{team.Id}/scopes/{Guid.NewGuid()}", null, Ct));

        Assert.Contains("Target", problem.Title);
    }

    /// <summary>
    /// Permissions are scoped within a view. Crossing views would let a view administrator grant into a
    /// view they hold nothing on.
    /// </summary>
    [Fact]
    public async Task Add_refuses_teams_in_different_views()
    {
        var view = TestData.View("First");
        var otherView = TestData.View("Second");
        var team = TestData.Team(view.Id);
        var target = TestData.Team(otherView.Id);
        await Seed(view, otherView, team, target);

        var problem = await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", null, Ct));

        Assert.Equal("Both Teams must belong to the same View.", problem.Title);
    }

    [Fact]
    public async Task Add_is_allowed_for_a_caller_holding_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Granting", role.Id);
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, role, team, target);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", null, Ct));

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionScopes.AnyAsync(
            x => x.TeamId == team.Id && x.TargetTeamId == target.Id, Ct));
    }

    [Fact]
    public async Task Add_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", null, Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionScopes.AnyAsync(Ct));
    }

    /// <summary>
    /// What a scope is for: the granting team's permissions start applying on the target team, so one of
    /// its members may act on a team they never joined.
    /// </summary>
    /// <remarks>
    /// Read through the endpoint the permission guards — <c>GET teams/{id}</c> needs <c>ViewTeam</c> on
    /// the team asked for. The 403 before the scope is what makes the 200 after it mean something, and
    /// the granting team's own role grants nothing, so the membership's <c>ViewTeam</c> is the only
    /// permission there is to project.
    /// </remarks>
    [Fact]
    public async Task Add_makes_the_granting_teams_permissions_apply_on_the_target_team()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Granting", role.Id);
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, role, team, target);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/teams/{target.Id}", Ct));

        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", null, Ct));

        var got = await ReadAsync<Team>(await Client(actor).GetAsync($"api/teams/{target.Id}", Ct));

        Assert.Equal(target.Id, got.Id);
    }

    // ---- Remove ---------------------------------------------------------------------------------

    [Fact]
    public async Task Remove_deletes_the_scope()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionScopes.AnyAsync(Ct));
    }

    /// <summary>
    /// Direction matters: the reverse scope is a separate grant and is left in place.
    /// </summary>
    [Fact]
    public async Task Remove_leaves_the_scope_in_the_other_direction_alone()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(
            view, team, target,
            TestData.TeamPermissionScope(team.Id, target.Id),
            TestData.TeamPermissionScope(target.Id, team.Id));

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", Ct));

        await using var db = NewContext();
        Assert.Equal(target.Id, (await db.TeamPermissionScopes.SingleAsync(Ct)).TeamId);
    }

    /// <summary>
    /// A scope that is not there is not an error — the request is already satisfied, and the response is
    /// the same 200 a removal answers with.
    /// </summary>
    [Fact]
    public async Task Remove_does_nothing_when_the_scope_is_absent()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", Ct));
    }

    [Fact]
    public async Task Remove_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", Ct));

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionScopes.AnyAsync(Ct));
    }

    /// <summary>
    /// The other half of the grant: the projected permission is gone on the next request, so a removal
    /// revokes rather than only deleting a row.
    /// </summary>
    /// <remarks>
    /// The harness turns claims caching off, so this pins the recomputation — the eviction that closes
    /// the same window in production is <c>AuthCacheEvictionTests</c>' subject.
    /// </remarks>
    [Fact]
    public async Task Remove_takes_the_scoped_permission_away()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Granting", role.Id);
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, role, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var got = await ReadAsync<Team>(await Client(actor).GetAsync($"api/teams/{target.Id}", Ct));
        Assert.Equal(target.Id, got.Id);

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/scopes/{target.Id}", Ct));

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/teams/{target.Id}", Ct));
    }
}
