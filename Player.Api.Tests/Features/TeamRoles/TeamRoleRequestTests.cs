// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamRoles;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamRoles;

/// <summary>
/// Covers the <c>TeamRoles</c> feature over HTTP, including the protection the two roles named in
/// <c>Roles:DefaultTeamRole</c> and <c>Roles:DefaultViewCreatorRole</c> get — <c>View Member</c> and
/// <c>View Admin</c>, from <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Team role names are uniquely indexed and a membership asking for specific permissions mints a role of
/// its own, so no test here counts the rows in <c>TeamRoles</c> or reads one without naming it. The seeded
/// rows are <c>View Admin</c> (immutable, all permissions), <c>View Member</c> and <c>Observer</c>.
/// </remarks>
public class TeamRoleRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_role()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/team-roles", new { name = "Red Team" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<TeamRole>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Red Team", created.Name);

        // CreatedAtRoute resolves "getTeamRole", so the Location header points at a route a client can
        // follow. The host is the test server's, so only the path is the endpoint's own doing.
        Assert.Equal($"/api/team-roles/{created.Id}", response.Headers.Location?.AbsolutePath);

        // The stored row rather than the response, since the response is mapped from the entity the
        // handler holds in memory and would read the same whether or not the save took the values with it.
        await using var db = NewContext();
        var stored = await db.TeamRoles.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Red Team", stored.Name);
    }

    /// <summary>
    /// The name is what a team role is assigned and configured by, so a duplicate is a conflict rather
    /// than a second row the unique index would refuse anyway.
    /// </summary>
    [Fact]
    public async Task Create_rejects_a_name_that_is_already_taken()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Conflict,
            await RootClient.PostAsJsonAsync("api/team-roles", new { name = "View Member" }, Ct));

        Assert.Equal("A role with that name already exists.", problem.Title);
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            "api/team-roles", new { name = "Nope" }, Ct));
    }

    // ---- Get ------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_role()
    {
        var got = await ReadAsync<TeamRole>(
            await RootClient.GetAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        Assert.Equal("Observer", got.Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_role_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/team-roles/{Guid.NewGuid()}", Ct));
    }

    // ---- GetAll ---------------------------------------------------------------------------------

    /// <summary>
    /// The seeded roles are enough: this asserts the handler does not filter, not how many rows exist.
    /// </summary>
    [Fact]
    public async Task GetAll_returns_every_role()
    {
        var roles = await ReadAsync<TeamRole[]>(await RootClient.GetAsync("api/team-roles", Ct));

        Assert.Contains(roles, x => x.Id == TestData.TeamRoles.ViewAdmin);
        Assert.Contains(roles, x => x.Id == TestData.TeamRoles.Observer);
        Assert.Contains(roles, x => x.Id == TestData.TeamRoles.ViewMember);
    }

    /// <summary>
    /// Team administrators need the list to assign roles, so <c>ManageTeam</c> on any team admits the
    /// caller without a system permission.
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

        Assert.NotEmpty(await ReadAsync<TeamRole[]>(
            await Client(actor).GetAsync("api/team-roles", Ct)));
    }

    /// <summary>The near miss is ViewTeam where the route admits ManageTeam on any team.</summary>
    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewTeam()
    {
        var view = TestData.View();
        await Seed(view);

        var actor = await Actor().OnNewTeam(view.Id, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync("api/team-roles", Ct));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_renames_the_role()
    {
        var edited = await ReadAsync<TeamRole>(await RootClient.PutAsJsonAsync(
            $"api/team-roles/{TestData.TeamRoles.Observer}", new { name = "Watcher" }, Ct));

        Assert.Equal("Watcher", edited.Name);

        await using var db = NewContext();
        Assert.Equal(
            "Watcher",
            (await db.TeamRoles.SingleAsync(x => x.Id == TestData.TeamRoles.Observer, Ct)).Name);
    }

    /// <summary>
    /// The configured defaults are resolved by name, so renaming one breaks every later create-team and
    /// create-view request.
    /// </summary>
    /// <remarks>
    /// The title is asserted because a name change can also collide with the unique index, and this test
    /// is about the options check rather than that.
    /// </remarks>
    [Theory]
    [InlineData("View Member")]
    [InlineData("View Admin")]
    public async Task Edit_refuses_to_rename_a_role_named_in_the_options(string name)
    {
        var role = await Db.TeamRoles.SingleAsync(x => x.Name == name, Ct);

        var problem = await AssertProblem(
            HttpStatusCode.Conflict,
            await RootClient.PutAsJsonAsync(
                $"api/team-roles/{role.Id}", new { name = "Something else" }, Ct));

        Assert.Equal(
            "Cannot change the Name of DefaultTeamRole (View Member) or " +
            "DefaultViewCreatorRole (View Admin)",
            problem.Title);
    }

    /// <summary>
    /// Only the name is protected. A default role's other properties stay editable, and the check is
    /// skipped entirely when the name is unchanged.
    /// </summary>
    [Fact]
    public async Task Edit_allows_other_changes_to_a_role_named_in_the_options()
    {
        var edited = await ReadAsync<TeamRole>(await RootClient.PutAsJsonAsync(
            $"api/team-roles/{TestData.TeamRoles.ViewMember}",
            new { name = "View Member", allPermissions = true },
            Ct));

        Assert.True(edited.AllPermissions);
    }

    [Fact]
    public async Task Edit_reports_a_missing_role_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/team-roles/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    /// <summary>A rename onto another team role's name is answered with a 500 and the stored name is kept.</summary>
    [Fact]
    public async Task Edit_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PutAsJsonAsync(
                $"api/team-roles/{TestData.TeamRoles.Observer}", new { name = "View Member" }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.StartsWith("An error occurred while saving the entity changes", problem.Detail);

        await using var db = NewContext();
        Assert.Equal(
            "Observer",
            (await db.TeamRoles.SingleAsync(x => x.Id == TestData.TeamRoles.Observer, Ct)).Name);
    }

    /// <summary>An immutable team role is renamed like any other and keeps its Immutable flag.</summary>
    [Fact]
    public async Task Edit_renames_an_immutable_role()
    {
        var role = ImmutableRole();
        await Seed(role);

        var edited = await ReadAsync<TeamRole>(await RootClient.PutAsJsonAsync(
            $"api/team-roles/{role.Id}", new { name = "Renamed" }, Ct));

        Assert.Equal("Renamed", edited.Name);
        Assert.True(edited.Immutable);
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_role()
    {
        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamRoles.AnyAsync(x => x.Id == TestData.TeamRoles.Observer, Ct));
    }

    /// <summary>
    /// Deleting either configured default would leave create-team and create-view resolving a role that
    /// is not there, so both are refused however the caller reaches them.
    /// </summary>
    [Theory]
    [InlineData("View Member")]
    [InlineData("View Admin")]
    public async Task Delete_refuses_a_role_named_in_the_options(string name)
    {
        var role = await Db.TeamRoles.SingleAsync(x => x.Name == name, Ct);

        var problem = await AssertProblem(
            HttpStatusCode.Conflict,
            await RootClient.DeleteAsync($"api/team-roles/{role.Id}", Ct));

        Assert.Equal("Cannot delete the DefaultTeamRole or View Admin", problem.Title);
    }

    [Fact]
    public async Task Delete_reports_a_missing_role_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/team-roles/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));
    }

    /// <summary>An immutable team role is deleted like any other.</summary>
    [Fact]
    public async Task Delete_removes_an_immutable_role()
    {
        var role = ImmutableRole();
        await Seed(role);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/team-roles/{role.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.TeamRoles.AnyAsync(x => x.Id == role.Id, Ct));
    }

    /// <summary>Deleting a team role a team uses deletes that team as well; the view is kept.</summary>
    [Fact]
    public async Task Delete_of_a_role_a_team_uses_deletes_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Doomed", TestData.TeamRoles.Observer);
        await Seed(view, team);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Teams.AnyAsync(x => x.Id == team.Id, Ct));

        // The view itself is kept; only the team that used the role is gone.
        Assert.True(await db.Views.AnyAsync(x => x.Id == view.Id, Ct));
    }

    /// <summary>Deleting a team role a membership uses is answered with a 500 and the role is kept.</summary>
    [Fact]
    public async Task Delete_of_a_role_a_membership_uses_is_a_server_error()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        await Actor().OnTeam(team, roleId: TestData.TeamRoles.Observer).SeedAsync();

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.DeleteAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.StartsWith("An error occurred while saving the entity changes", problem.Detail);

        await using var db = NewContext();
        Assert.True(await db.TeamRoles.AnyAsync(x => x.Id == TestData.TeamRoles.Observer, Ct));
    }

    // ---- Who may call ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_is_allowed_for_a_caller_holding_only_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var created = await ReadAsync<TeamRole>(await Client(actor).PostAsJsonAsync(
            "api/team-roles", new { name = "Created By A Role Manager" }, Ct));

        Assert.True(await ReadBack(db => db.TeamRoles.AnyAsync(x => x.Id == created.Id, Ct)));
    }

    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_only_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var got = await ReadAsync<TeamRole>(
            await Client(actor).GetAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        Assert.Equal("Observer", got.Name);
    }

    /// <summary>Team administrators read the role they assign, so ManageTeam on any team admits the caller.</summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_only_ManageTeam()
    {
        var view = TestData.View();
        await Seed(view);
        var actor = await Actor().OnNewTeam(view.Id, teamPermissions: [TeamPermission.ManageTeam]).SeedAsync();

        var got = await ReadAsync<TeamRole>(
            await Client(actor).GetAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        Assert.Equal("Observer", got.Name);
    }

    /// <summary>The near miss is ViewTeam where the route admits ManageTeam on any team.</summary>
    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewTeam()
    {
        var view = TestData.View();
        await Seed(view);
        var actor = await Actor().OnNewTeam(view.Id, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));
    }

    [Fact]
    public async Task Edit_is_allowed_for_a_caller_holding_only_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PutAsJsonAsync(
            $"api/team-roles/{TestData.TeamRoles.Observer}", new { name = "Renamed By A Role Manager" }, Ct));

        Assert.Equal(
            "Renamed By A Role Manager",
            await ReadBack(db => db.TeamRoles.Where(x => x.Id == TestData.TeamRoles.Observer).Select(x => x.Name).SingleAsync(Ct)));
    }

    /// <summary>
    /// The near miss is ManageTeam, which reads team roles but does not change them; the name stays.
    /// </summary>
    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ManageTeam()
    {
        var view = TestData.View();
        await Seed(view);
        var actor = await Actor().OnNewTeam(view.Id, teamPermissions: [TeamPermission.ManageTeam]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync(
            $"api/team-roles/{TestData.TeamRoles.Observer}", new { name = "Not Renamed" }, Ct));

        Assert.Equal(
            "Observer",
            await ReadBack(db => db.TeamRoles.Where(x => x.Id == TestData.TeamRoles.Observer).Select(x => x.Name).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Delete_is_allowed_for_a_caller_holding_only_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        await AssertStatus(
            HttpStatusCode.NoContent,
            await Client(actor).DeleteAsync($"api/team-roles/{TestData.TeamRoles.Observer}", Ct));

        Assert.False(await ReadBack(db => db.TeamRoles.AnyAsync(x => x.Id == TestData.TeamRoles.Observer, Ct)));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// A team role marked immutable, under a name nothing else can hold. Immutability is a property of
    /// the row, and the two tests that are about it need one the options check does not reach first.
    /// </summary>
    private static TeamRoleEntity ImmutableRole() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = $"Immutable Team Role {Guid.NewGuid():N}",
            Immutable = true
        };
}
