// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamPermissions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamPermissions;

/// <summary>
/// Covers the <c>TeamPermissions</c> feature over HTTP: the permission rows themselves, their grants to
/// roles and to individual teams, and the claims-only <c>GetMine</c> query.
/// </summary>
public class TeamPermissionRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_permission()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/team-permissions",
            new { name = "Custom", description = "A custom one" },
            Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<TeamPermissionModel>(response);

        Assert.Equal("Custom", created.Name);

        // The only assertion in this file on the route name CreatedAtRoute resolves, which is what makes
        // the Location header point at something a client can follow.
        Assert.Equal($"/api/team-permissions/{created.Id}", response.Headers.Location?.AbsolutePath);

        // The stored row rather than the response, since the response is mapped from the entity the
        // handler holds in memory and would read the same whether or not the save took the values with it.
        await using var db = NewContext();
        var stored = await db.TeamPermissions.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Custom", stored.Name);
        Assert.Equal("A custom one", stored.Description);
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            "api/team-permissions", new { name = "Nope" }, Ct));
    }

    [Fact]
    public async Task Edit_updates_a_mutable_permission()
    {
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        var edited = await ReadAsync<TeamPermissionModel>(await RootClient.PutAsJsonAsync(
            $"api/team-permissions/{permissionId}",
            new { name = "UploadViewIsos", description = "Reworded" },
            Ct));

        Assert.Equal("Reworded", edited.Description);

        await using var db = NewContext();
        Assert.Equal(
            "Reworded",
            (await db.TeamPermissions.SingleAsync(x => x.Id == permissionId, Ct)).Description);
    }

    /// <summary>
    /// The immutable flag protects the permissions team authorization resolves by name: renaming one
    /// would silently revoke it from every role and team that grants it.
    /// </summary>
    /// <remarks>
    /// The title is asserted because the caller holds every system permission, so it is what says the
    /// 403 is the flag's refusal rather than authorization's.
    /// </remarks>
    [Fact]
    public async Task Edit_refuses_an_immutable_permission()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Forbidden,
            await RootClient.PutAsJsonAsync(
                $"api/team-permissions/{TestData.TeamPermissions.ViewTeam}",
                new { name = "Renamed" },
                Ct));

        Assert.Equal("Cannot update an Immutable TeamPermissionModel", problem.Title);
    }

    [Fact]
    public async Task Edit_reports_a_missing_permission_as_not_found()
    {
        await AssertNotFound("Team Permission Model", await RootClient.PutAsJsonAsync(
            $"api/team-permissions/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    [Fact]
    public async Task Delete_removes_a_mutable_permission()
    {
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/team-permissions/{permissionId}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamPermissions.AnyAsync(x => x.Id == permissionId, Ct));
    }

    [Fact]
    public async Task Delete_refuses_an_immutable_permission()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Forbidden,
            await RootClient.DeleteAsync(
                $"api/team-permissions/{TestData.TeamPermissions.ViewTeam}", Ct));

        Assert.Equal("Cannot delete a Read-Only TeamPermissionModel", problem.Title);
    }

    [Fact]
    public async Task Delete_reports_a_missing_permission_as_not_found()
    {
        await AssertNotFound(
            "Team Permission Model",
            await RootClient.DeleteAsync($"api/team-permissions/{Guid.NewGuid()}", Ct));
    }

    // ---- Get / GetAll ---------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_permission()
    {
        var got = await ReadAsync<TeamPermissionModel>(await RootClient.GetAsync(
            $"api/team-permissions/{TestData.TeamPermissions.ViewTeam}", Ct));

        Assert.Equal(TeamPermission.ViewTeam.ToString(), got.Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_permission_as_not_found()
    {
        await AssertNotFound(
            "Team Permission Model",
            await RootClient.GetAsync($"api/team-permissions/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync(
            $"api/team-permissions/{TestData.TeamPermissions.ViewTeam}", Ct));
    }

    [Fact]
    public async Task GetAll_returns_every_permission()
    {
        var permissions = await ReadAsync<TeamPermissionModel[]>(
            await RootClient.GetAsync("api/team-permissions", Ct));

        Assert.Contains(permissions, x => x.Id == TestData.TeamPermissions.ViewTeam);
        Assert.Contains(permissions, x => x.Id == TestData.TeamPermissions.UploadViewIsos);
    }

    /// <summary>
    /// Team administrators need the list to grant permissions, so <c>ManageTeam</c> on any team admits
    /// the caller without a system permission.
    /// </summary>
    /// <remarks>
    /// The team's own role grants nothing, so the permission the membership names is the only one the
    /// caller holds and the only thing that could have admitted them.
    /// </remarks>
    [Fact]
    public async Task GetAll_is_allowed_for_a_caller_holding_ManageTeam_on_any_team()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor()
            .OnTeam(team, teamPermissions: [TeamPermission.ManageTeam])
            .SeedAsync();

        Assert.NotEmpty(await ReadAsync<TeamPermissionModel[]>(
            await Client(actor).GetAsync("api/team-permissions", Ct)));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_with_no_permissions()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync("api/team-permissions", Ct));
    }

    // ---- GetMine ---------------------------------------------------------------------------------

    /// <summary>
    /// Open to any caller and answered from the claims: it reports what the caller holds, so there is
    /// nothing to authorize.
    /// </summary>
    /// <remarks>
    /// All three query parameters are nullable, so the bare route binds — unlike the export and import
    /// flags elsewhere, which a request must supply.
    /// </remarks>
    [Fact]
    public async Task GetMine_returns_every_team_claim_when_unfiltered()
    {
        var first = TestData.View("First");
        var second = TestData.View("Second");
        var firstTeam = TestData.Team(first.Id, "First team");
        var secondTeam = TestData.Team(second.Id, "Second team");
        await Seed(first, second, firstTeam, secondTeam);

        var actor = await Actor().OnTeam(firstTeam).OnTeam(secondTeam).SeedAsync();

        var claims = await ReadAsync<TeamPermissionsClaim[]>(
            await Client(actor).GetAsync("api/team-permissions/mine", Ct));

        // Both claims named, and the count as the bound on "every": a count of two also holds if one team
        // were reported twice and the other view left out altogether.
        Assert.Equal(2, claims.Length);
        Assert.Contains(claims, x => x.TeamId == firstTeam.Id && x.ViewId == first.Id);
        Assert.Contains(claims, x => x.TeamId == secondTeam.Id && x.ViewId == second.Id);
    }

    [Fact]
    public async Task GetMine_returns_nothing_for_a_caller_on_no_teams()
    {
        var actor = await Actor().SeedAsync();

        Assert.Empty(await ReadAsync<TeamPermissionsClaim[]>(
            await Client(actor).GetAsync("api/team-permissions/mine", Ct)));
    }

    [Fact]
    public async Task GetMine_filters_by_view()
    {
        var view = TestData.View();
        var elsewhere = TestData.View("Elsewhere");
        var firstTeam = TestData.Team(view.Id, "First");
        var secondTeam = TestData.Team(view.Id, "Second");
        var otherTeam = TestData.Team(elsewhere.Id, "Other");
        await Seed(view, elsewhere, firstTeam, secondTeam, otherTeam);

        var actor = await Actor()
            .OnTeam(firstTeam)
            .OnTeam(secondTeam)
            .OnTeam(otherTeam)
            .SeedAsync();

        var claims = await ReadAsync<TeamPermissionsClaim[]>(await Client(actor).GetAsync(
            $"api/team-permissions/mine?viewId={view.Id}", Ct));

        Assert.Equal(2, claims.Length);
        Assert.All(claims, x => Assert.Equal(view.Id, x.ViewId));
    }

    /// <summary>
    /// A view filter wins: the team filter is only consulted when no view was given.
    /// </summary>
    [Fact]
    public async Task GetMine_ignores_the_team_when_a_view_is_also_given()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "First");
        var otherTeam = TestData.Team(view.Id, "Second");
        await Seed(view, team, otherTeam);

        var actor = await Actor().OnTeam(team).OnTeam(otherTeam).SeedAsync();

        var claims = await ReadAsync<TeamPermissionsClaim[]>(await Client(actor).GetAsync(
            $"api/team-permissions/mine?viewId={view.Id}&teamId={team.Id}", Ct));

        // The team named in the query string is the one that would have been kept had the team filter won,
        // so the assertion has to name the other one to say the filter was ignored.
        Assert.Equal(2, claims.Length);
        Assert.Contains(claims, x => x.TeamId == team.Id);
        Assert.Contains(claims, x => x.TeamId == otherTeam.Id);
    }

    [Fact]
    public async Task GetMine_filters_by_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "First");
        var otherTeam = TestData.Team(view.Id, "Second");
        await Seed(view, team, otherTeam);

        var actor = await Actor().OnTeam(team).OnTeam(otherTeam).SeedAsync();

        var claims = await ReadAsync<TeamPermissionsClaim[]>(await Client(actor).GetAsync(
            $"api/team-permissions/mine?teamId={team.Id}", Ct));

        Assert.Equal(team.Id, Assert.Single(claims).TeamId);
    }

    /// <summary>
    /// A team the caller is on widens to every team of that view the caller is on, with the view read
    /// off the matching claim.
    /// </summary>
    [Fact]
    public async Task GetMine_widens_to_the_view_of_a_team_the_caller_is_on()
    {
        var view = TestData.View();
        var elsewhere = TestData.View("Elsewhere");
        var team = TestData.Team(view.Id, "First");
        var otherTeam = TestData.Team(view.Id, "Second");
        var elsewhereTeam = TestData.Team(elsewhere.Id, "Other");
        await Seed(view, elsewhere, team, otherTeam, elsewhereTeam);

        var actor = await Actor()
            .OnTeam(team)
            .OnTeam(otherTeam)
            .OnTeam(elsewhereTeam)
            .SeedAsync();

        var claims = await ReadAsync<TeamPermissionsClaim[]>(await Client(actor).GetAsync(
            $"api/team-permissions/mine?teamId={team.Id}&includeAllViewTeams=true", Ct));

        Assert.Equal(2, claims.Length);
        Assert.All(claims, x => Assert.Equal(view.Id, x.ViewId));
    }

    /// <summary>
    /// With no claim for the requested team the view has to be looked up, which is how a caller who is
    /// on one team in a view can discover their permissions on the others.
    /// </summary>
    [Fact]
    public async Task GetMine_looks_up_the_view_of_a_team_the_caller_is_not_on()
    {
        var view = TestData.View();
        var elsewhere = TestData.View("Elsewhere");
        var known = TestData.Team(view.Id, "Known");
        var unknown = TestData.Team(view.Id, "Unknown");
        var elsewhereTeam = TestData.Team(elsewhere.Id, "Other");
        await Seed(view, elsewhere, known, unknown, elsewhereTeam);

        var actor = await Actor().OnTeam(known).OnTeam(elsewhereTeam).SeedAsync();

        var claims = await ReadAsync<TeamPermissionsClaim[]>(await Client(actor).GetAsync(
            $"api/team-permissions/mine?teamId={unknown.Id}&includeAllViewTeams=true", Ct));

        Assert.Equal(known.Id, Assert.Single(claims).TeamId);
    }

    // ---- AddToRole / RemoveFromRole -------------------------------------------------------------

    [Fact]
    public async Task AddToRole_grants_the_permission_to_the_role()
    {
        var roleId = TestData.TeamRoles.Observer;
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync(
            $"api/team-roles/{roleId}/permissions/{permissionId}", null, Ct));

        await using var db = NewContext();
        Assert.True(await db.TeamRolePermissions.AnyAsync(
            x => x.RoleId == roleId && x.PermissionId == permissionId, Ct));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_role_as_not_found()
    {
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertNotFound("Team Role", await RootClient.PostAsync(
            $"api/team-roles/{Guid.NewGuid()}/permissions/{permissionId}", null, Ct));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_permission_as_not_found()
    {
        var roleId = TestData.TeamRoles.Observer;

        await AssertNotFound("Team Permission Model", await RootClient.PostAsync(
            $"api/team-roles/{roleId}/permissions/{Guid.NewGuid()}", null, Ct));
    }

    [Fact]
    public async Task AddToRole_is_forbidden_without_ManageRoles()
    {
        var roleId = TestData.TeamRoles.Observer;
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(
            $"api/team-roles/{roleId}/permissions/{permissionId}", null, Ct));
    }

    [Fact]
    public async Task RemoveFromRole_revokes_the_permission()
    {
        var roleId = TestData.TeamRoles.Observer;
        var permissionId = TestData.TeamPermissions.UploadViewIsos;
        await Seed(new TeamRolePermissionEntity(roleId, permissionId) { Id = Guid.NewGuid() });

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/team-roles/{roleId}/permissions/{permissionId}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamRolePermissions.AnyAsync(
            x => x.RoleId == roleId && x.PermissionId == permissionId, Ct));
    }

    /// <summary>
    /// A grant that is not there is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task RemoveFromRole_does_nothing_when_the_role_does_not_hold_the_permission()
    {
        var roleId = TestData.TeamRoles.Observer;
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/team-roles/{roleId}/permissions/{permissionId}", Ct));
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_role_as_not_found()
    {
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertNotFound("Team Role", await RootClient.DeleteAsync(
            $"api/team-roles/{Guid.NewGuid()}/permissions/{permissionId}", Ct));
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_permission_as_not_found()
    {
        var roleId = TestData.TeamRoles.Observer;

        await AssertNotFound("Team Permission Model", await RootClient.DeleteAsync(
            $"api/team-roles/{roleId}/permissions/{Guid.NewGuid()}", Ct));
    }

    // ---- AddToTeam / RemoveFromTeam -------------------------------------------------------------

    /// <summary>
    /// A grant to one team, outside any role — how a single team gets a permission its role does not
    /// carry.
    /// </summary>
    [Fact]
    public async Task AddToTeam_grants_the_permission_to_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync(
            $"api/teams/{team.Id}/permissions/{permissionId}", null, Ct));

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionAssignments.AnyAsync(
            x => x.TeamId == team.Id && x.PermissionId == permissionId, Ct));
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_team_as_not_found()
    {
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertNotFound("Team", await RootClient.PostAsync(
            $"api/teams/{Guid.NewGuid()}/permissions/{permissionId}", null, Ct));
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_permission_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await AssertNotFound("Team Permission Model", await RootClient.PostAsync(
            $"api/teams/{team.Id}/permissions/{Guid.NewGuid()}", null, Ct));
    }

    /// <summary>
    /// Scoped to the team, so <c>ManageView</c> on the containing view is enough — a system permission
    /// is not required.
    /// </summary>
    [Fact]
    public async Task AddToTeam_is_allowed_for_a_caller_holding_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor()
            .OnTeam(team, viewPermissions: [ViewPermission.ManageView])
            .SeedAsync();

        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PostAsync(
            $"api/teams/{team.Id}/permissions/{permissionId}", null, Ct));

        await using var db = NewContext();
        // Naming the permission as well as the team, since the team holding some other permission is not
        // what the request asked for.
        Assert.True(await db.TeamPermissionAssignments.AnyAsync(
            x => x.TeamId == team.Id && x.PermissionId == permissionId, Ct));
    }

    [Fact]
    public async Task AddToTeam_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var actor = await Actor().SeedAsync();
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(
            $"api/teams/{team.Id}/permissions/{permissionId}", null, Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_revokes_the_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var permissionId = TestData.TeamPermissions.UploadViewIsos;
        await Seed(
            view,
            team,
            new TeamPermissionAssignmentEntity(team.Id, permissionId) { Id = Guid.NewGuid() });

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/permissions/{permissionId}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionAssignments.AnyAsync(x => x.TeamId == team.Id, Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_does_nothing_when_the_team_does_not_hold_the_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/permissions/{permissionId}", Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_team_as_not_found()
    {
        var permissionId = TestData.TeamPermissions.UploadViewIsos;

        await AssertNotFound("Team", await RootClient.DeleteAsync(
            $"api/teams/{Guid.NewGuid()}/permissions/{permissionId}", Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_permission_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await AssertNotFound("Team Permission Model", await RootClient.DeleteAsync(
            $"api/teams/{team.Id}/permissions/{Guid.NewGuid()}", Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Asserts a 404 naming <paramref name="entity"/>, which is what tells the two not-found cases of
    /// one route apart: <c>EntityNotFoundException&lt;T&gt;</c> builds its message from the type name
    /// and <c>ExceptionMiddleware</c> answers with the message as the title.
    /// </summary>
    private static async Task AssertNotFound(string entity, HttpResponseMessage response) =>
        Assert.Equal(
            $"{entity} not found",
            (await AssertProblem(HttpStatusCode.NotFound, response)).Title);
}
