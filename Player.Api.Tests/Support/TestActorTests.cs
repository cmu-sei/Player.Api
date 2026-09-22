// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Options;
using Player.Api.Services;

namespace Player.Api.Tests.Support;

/// <summary>
/// Tests for <see cref="TestActorBuilder"/>, the seeding side of every HTTP test's setup.
/// </summary>
/// <remarks>
/// These run the real <c>UserClaimsService</c> over the seeded rows rather than making a request,
/// because a status code cannot tell an effective permission from a direct one, nor a primary team
/// from a second team. An actor that quietly holds more than the test asked for turns an
/// authorization test into a formality.
/// </remarks>
public class TestActorTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    // ---- System permissions -----------------------------------------------------------------------

    /// <summary>
    /// The seeded <c>Administrator</c> role has <c>AllPermissions</c>, which the service resolves
    /// against the permission table — so the actor holds whatever a migration put there.
    /// </summary>
    [Fact]
    public async Task WithAllSystemPermissions_grants_every_stored_permission()
    {
        var actor = await Actor().WithAllSystemPermissions().SeedAsync();

        var stored = await Db.Permissions.Select(x => x.Name).ToArrayAsync(Ct);
        Assert.Equal(stored.Order(), Permissions(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task WithSystemPermissions_grants_exactly_what_it_names()
    {
        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.CreateViews, SystemPermission.ViewViews)
            .SeedAsync();

        Assert.Equal(["CreateViews", "ViewViews"], Permissions(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task WithRole_grants_that_roles_permissions()
    {
        var actor = await Actor().WithRole(TestData.Roles.ContentDeveloper).SeedAsync();

        var granted = await Db.RolePermissions
            .Where(x => x.RoleId == TestData.Roles.ContentDeveloper)
            .Select(x => x.Permission.Name)
            .ToArrayAsync(Ct);

        Assert.NotEmpty(granted);
        Assert.Equal(granted.Order(), Permissions(await ClaimsOf(actor)));
    }

    /// <summary>
    /// The baseline an authorization test starts from: an actor who is authenticated and holds nothing.
    /// </summary>
    [Fact]
    public async Task An_actor_with_no_role_and_no_team_holds_nothing()
    {
        var actor = await Actor().SeedAsync();

        var principal = await ClaimsOf(actor);

        Assert.Empty(Permissions(principal));
        Assert.Empty(TeamClaims(principal));
    }

    [Fact]
    public async Task The_user_row_carries_the_id_and_name()
    {
        var id = Guid.NewGuid();

        var actor = await Actor().WithId(id).WithName("Named").SeedAsync();

        Assert.Equal(id, actor.Id);
        await using var context = NewContext();
        var user = await context.Users.SingleAsync(x => x.Id == id, Ct);
        Assert.Equal("Named", user.Name);
    }

    // ---- Team permissions -------------------------------------------------------------------------

    /// <summary>
    /// A membership on a team that grants nothing: the claim exists, naming the view and the team, and
    /// carries no permissions.
    /// </summary>
    [Fact]
    public async Task A_membership_on_a_permission_free_team_grants_nothing()
    {
        var view = await SeedView();
        var team = await SeedPermissionFreeTeam(view);

        var actor = await Actor().OnTeam(team).SeedAsync();

        var claim = TeamClaim(await ClaimsOf(actor), team.Id);
        Assert.Equal(view.Id, claim.ViewId);
        Assert.Empty(claim.PermissionValues);
        Assert.Equal([team.Id], claim.SourceTeamIds);
    }

    /// <summary>
    /// The permissions passed to <c>OnTeam</c> are the actor's permissions on that team and nothing
    /// else — both enums name rows in the one table, so a view permission and a team permission arrive
    /// in the same claim.
    /// </summary>
    [Fact]
    public async Task OnTeam_permissions_are_the_only_ones_the_membership_carries()
    {
        var view = await SeedView();
        var team = await SeedPermissionFreeTeam(view);

        var actor = await Actor()
            .OnTeam(team, viewPermissions: [ViewPermission.EditView], teamPermissions: [TeamPermission.ViewTeam])
            .SeedAsync();

        var claim = TeamClaim(await ClaimsOf(actor), team.Id);
        Assert.Equal(["EditView", "ViewTeam"], claim.PermissionValues.Order());
        Assert.Equal([ViewPermission.EditView], claim.ViewPermissions);
        Assert.Equal([TeamPermission.ViewTeam], claim.TeamPermissions);
    }

    /// <summary>
    /// A membership's own permissions are direct, which is what <c>PrimaryVisibilityContext</c> and the
    /// scoped-permission checks read.
    /// </summary>
    [Fact]
    public async Task A_memberships_permissions_are_direct_permissions()
    {
        var view = await SeedView();
        var team = await SeedPermissionFreeTeam(view);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var claim = TeamClaim(await ClaimsOf(actor), team.Id);
        Assert.Equal(["ViewTeam"], claim.DirectPermissionValues);
    }

    /// <summary>
    /// <c>View Admin</c> has <c>AllPermissions</c>, so naming it grants every stored team permission
    /// rather than the ones the role was granted.
    /// </summary>
    [Fact]
    public async Task A_membership_role_of_view_admin_grants_every_team_permission()
    {
        var view = await SeedView();
        var team = await SeedPermissionFreeTeam(view);

        var actor = await Actor().OnTeam(team, roleId: TestData.TeamRoles.ViewAdmin).SeedAsync();

        var stored = await Db.TeamPermissions.Select(x => x.Name).ToArrayAsync(Ct);
        var claim = TeamClaim(await ClaimsOf(actor), team.Id);
        Assert.Equal(stored.Order(), claim.PermissionValues.Order());
    }

    /// <summary>
    /// What the team grants reaches the membership too, which is why a test wanting exactly its own
    /// permissions puts the actor on a permission-free team.
    /// </summary>
    [Fact]
    public async Task A_membership_also_carries_what_the_teams_own_role_grants()
    {
        var view = await SeedView();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.ViewMember);
        await Seed(team);

        var actor = await Actor().OnTeam(team).SeedAsync();

        var granted = await Db.TeamRolePermissions
            .Where(x => x.RoleId == TestData.TeamRoles.ViewMember)
            .Select(x => x.Permission.Name)
            .ToArrayAsync(Ct);

        Assert.NotEmpty(granted);
        Assert.Equal(granted.Order(), TeamClaim(await ClaimsOf(actor), team.Id).PermissionValues.Order());
    }

    // ---- Memberships ------------------------------------------------------------------------------

    /// <summary>
    /// One view membership however many teams in the view, and the first team declared is the primary
    /// one — what a sequence of <c>Users/AddToTeam</c> requests would have left.
    /// </summary>
    [Fact]
    public async Task Two_teams_in_one_view_share_a_view_membership_and_the_first_is_primary()
    {
        var view = await SeedView();
        var first = await SeedPermissionFreeTeam(view, "First");
        var second = await SeedPermissionFreeTeam(view, "Second");

        var actor = await Actor().OnTeam(first).OnTeam(second).SeedAsync();

        Assert.Equal(actor.On(first.Id).ViewMembershipId, actor.On(second.Id).ViewMembershipId);
        Assert.True(actor.On(first.Id).IsPrimary);
        Assert.False(actor.On(second.Id).IsPrimary);

        var principal = await ClaimsOf(actor);
        Assert.True(TeamClaim(principal, first.Id).IsPrimary);
        Assert.False(TeamClaim(principal, second.Id).IsPrimary);
    }

    [Fact]
    public async Task The_team_marked_primary_is_the_primary_one_whatever_the_order()
    {
        var view = await SeedView();
        var first = await SeedPermissionFreeTeam(view, "First");
        var second = await SeedPermissionFreeTeam(view, "Second");

        var actor = await Actor().OnTeam(first).OnTeam(second, primary: true).SeedAsync();

        Assert.False(actor.On(first.Id).IsPrimary);
        Assert.True(actor.On(second.Id).IsPrimary);

        var principal = await ClaimsOf(actor);
        Assert.False(TeamClaim(principal, first.Id).IsPrimary);
        Assert.True(TeamClaim(principal, second.Id).IsPrimary);
    }

    /// <summary>
    /// A view membership per view, each with its own primary team — the arrangement the visibility
    /// rules are written against.
    /// </summary>
    [Fact]
    public async Task Teams_in_two_views_get_a_view_membership_each()
    {
        var here = await SeedView("Here");
        var there = await SeedView("There");
        var hereTeam = await SeedPermissionFreeTeam(here);
        var thereTeam = await SeedPermissionFreeTeam(there);

        var actor = await Actor().OnTeam(hereTeam).OnTeam(thereTeam).SeedAsync();

        Assert.NotEqual(actor.On(hereTeam.Id).ViewMembershipId, actor.On(thereTeam.Id).ViewMembershipId);
        Assert.All(actor.Memberships, x => Assert.True(x.IsPrimary));

        var principal = await ClaimsOf(actor);
        Assert.Equal(here.Id, TeamClaim(principal, hereTeam.Id).ViewId);
        Assert.Equal(there.Id, TeamClaim(principal, thereTeam.Id).ViewId);
    }

    /// <summary>
    /// A scope carries the granting team's permissions onto a team the actor is not in: the target
    /// claim names the granting team as its source and holds no direct permissions.
    /// </summary>
    [Fact]
    public async Task A_scope_puts_the_granting_teams_permissions_on_the_target_team()
    {
        var view = await SeedView();
        var granting = await SeedPermissionFreeTeam(view, "Granting");
        var target = await SeedPermissionFreeTeam(view, "Target");
        await Seed(TestData.TeamPermissionScope(granting.Id, target.Id));

        var actor = await Actor()
            .OnTeam(granting, teamPermissions: [TeamPermission.ViewTeam])
            .SeedAsync();

        var claim = TeamClaim(await ClaimsOf(actor), target.Id);
        Assert.Equal(["ViewTeam"], claim.PermissionValues);
        Assert.Empty(claim.DirectPermissionValues);
        Assert.Equal([granting.Id], claim.SourceTeamIds);
        Assert.False(claim.IsPrimary);
    }

    // ---- Guards -----------------------------------------------------------------------------------

    /// <summary>
    /// Both calls decide the same column, and the second would silently win.
    /// </summary>
    [Fact]
    public void WithSystemPermissions_after_WithRole_throws()
    {
        var builder = Actor().WithRole(TestData.Roles.Administrator);

        Assert.Throws<InvalidOperationException>(
            () => builder.WithSystemPermissions(SystemPermission.ViewViews));
    }

    [Fact]
    public void WithRole_after_WithSystemPermissions_throws()
    {
        var builder = Actor().WithSystemPermissions(SystemPermission.ViewViews);

        Assert.Throws<InvalidOperationException>(() => builder.WithRole(TestData.Roles.Administrator));
    }

    /// <summary>
    /// A membership has one role, so naming one and asking for permissions — which mints another — is
    /// a contradiction rather than a union.
    /// </summary>
    [Fact]
    public async Task OnTeam_with_both_a_role_and_permissions_throws()
    {
        var view = await SeedView();
        var team = await SeedPermissionFreeTeam(view);

        Assert.Throws<InvalidOperationException>(() => Actor().OnTeam(
            team,
            teamPermissions: [TeamPermission.ViewTeam],
            roleId: TestData.TeamRoles.Observer));
    }

    [Fact]
    public void OnTeam_a_team_with_no_view_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Actor().OnTeam(TestData.Team(Guid.Empty)));
    }

    [Fact]
    public async Task Two_primary_teams_in_one_view_throw()
    {
        var view = await SeedView();
        var first = await SeedPermissionFreeTeam(view, "First");
        var second = await SeedPermissionFreeTeam(view, "Second");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Actor()
            .OnTeam(first, primary: true)
            .OnTeam(second, primary: true)
            .SeedAsync());
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private TestActorBuilder Actor() => new(Db, Ct);

    /// <summary>
    /// The claims the real service derives from what the builder seeded. Caching is off, so each call
    /// re-reads the rows.
    /// </summary>
    private async Task<ClaimsPrincipal> ClaimsOf(TestActor actor)
    {
        var service = new UserClaimsService(
            Db,
            new MemoryCache(new MemoryCacheOptions()),
            new ClaimsTransformationOptions { EnableCaching = false, UseRolesFromIdP = false },
            TestMapper.Mapper);

        return await service.GetClaimsPrincipal(actor.Id, true);
    }

    private static string[] Permissions(ClaimsPrincipal principal) =>
    [
        .. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.PermissionsClaimType)
            .Select(x => x.Value)
            .Order()
    ];

    private static TeamPermissionsClaim[] TeamClaims(ClaimsPrincipal principal) =>
    [
        .. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.TeamPermissionsClaimType)
            .Select(x => TeamPermissionsClaim.FromString(x.Value))
    ];

    /// <summary>The one claim for <paramref name="teamId"/>. Fails if there is none, or more than one.</summary>
    private static TeamPermissionsClaim TeamClaim(ClaimsPrincipal principal, Guid teamId) =>
        Assert.Single(TeamClaims(principal), x => x.TeamId == teamId);

    private async Task<ViewEntity> SeedView(string name = "Test View")
    {
        var view = TestData.View(name);
        await Seed(view);

        return view;
    }

    /// <summary>
    /// A team whose role grants nothing, so the actor's permissions on it are only what the membership
    /// carries. <see cref="TestData.Team"/> defaults to <c>View Member</c>, which grants four.
    /// </summary>
    private async Task<TeamEntity> SeedPermissionFreeTeam(ViewEntity view, string name = "Test Team")
    {
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, name, role.Id);
        await Seed(role, team);

        return team;
    }
}
