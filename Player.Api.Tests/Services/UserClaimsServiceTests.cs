// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Options;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Services;

/// <summary>
/// Turns a user's rows into the claims every authorization decision is then made from. What this
/// produces is the input to <c>SystemPermissionsHandler</c> and <c>TeamPermissionsHandler</c>, so a
/// permission that never becomes a claim here can never be granted.
/// </summary>
public class UserClaimsServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    // ---- Membership claims ------------------------------------------------------------------------

    /// <summary>
    /// One view claim however many teams the user is in, because the claim answers "is a member of" and
    /// not "how many times".
    /// </summary>
    [Fact]
    public async Task Claims_carry_one_view_member_claim_and_one_team_member_claim_per_team()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        await Seed(view, first, second);
        var user = await SeedMember(view, first, second);

        var principal = await Service().GetClaimsPrincipal(user.Id, true);

        Assert.Equal([view.Id.ToString()], Values(principal, PlayerClaimTypes.ViewMember.ToString()));
        Assert.Equal(
            new[] { first.Id.ToString(), second.Id.ToString() }.Order(),
            Values(principal, PlayerClaimTypes.TeamMember.ToString()).Order());
    }

    [Fact]
    public async Task Claims_name_the_primary_team()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        await Seed(view, first, second);
        var user = await SeedMember(view, first, second);

        var principal = await Service().GetClaimsPrincipal(user.Id, true);

        Assert.Equal([first.Id.ToString()], Values(principal, PlayerClaimTypes.PrimaryTeam.ToString()));
    }

    [Fact]
    public async Task Claims_are_empty_for_a_user_with_no_memberships()
    {
        var user = TestData.User();
        await Seed(user);

        var principal = await Service().GetClaimsPrincipal(user.Id, true);

        Assert.Empty(Values(principal, PlayerClaimTypes.ViewMember.ToString()));
        Assert.Empty(Values(principal, AuthorizationConstants.PermissionsClaimType));
        Assert.Empty(Values(principal, AuthorizationConstants.TeamPermissionsClaimType));
    }

    // ---- System permission claims -----------------------------------------------------------------

    [Fact]
    public async Task Claims_carry_the_permissions_of_the_users_role()
    {
        var role = await SeedRole("Ops", SystemPermission.CreateViews, SystemPermission.ViewViews);
        var user = TestData.User(roleId: role.Id);
        await Seed(user);

        var principal = await Service().GetClaimsPrincipal(user.Id, true);

        Assert.Equal(
            ["CreateViews", "ViewViews"],
            Values(principal, AuthorizationConstants.PermissionsClaimType).Order());
    }

    /// <summary>
    /// A role marked <c>AllPermissions</c> is resolved against the permission table rather than its own
    /// grants, so a permission added by a later migration is included without touching the role.
    /// </summary>
    [Fact]
    public async Task Claims_expand_an_all_permissions_role_to_every_stored_permission()
    {
        var user = TestData.User(roleId: TestData.Roles.Administrator);
        await Seed(user);

        var principal = await Service().GetClaimsPrincipal(user.Id, true);

        var stored = await Db.Permissions.Select(x => x.Name).ToArrayAsync(Ct);
        Assert.Equal(
            stored.Order(),
            Values(principal, AuthorizationConstants.PermissionsClaimType).Order());
    }

    // ---- Team permission claims -------------------------------------------------------------------

    /// <summary>
    /// A team claim's effective permissions are the union of three sources: the team's own assignments,
    /// its default role, and the role on the membership.
    /// </summary>
    [Fact]
    public async Task Team_claims_union_the_team_assignments_the_team_role_and_the_membership_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.Observer);
        await Seed(view, team);
        await Seed(new TeamPermissionAssignmentEntity(team.Id, TestData.TeamPermissions.UploadViewIsos));
        var user = await SeedMember(view, team, membershipRoleId: TestData.TeamRoles.ViewMember);

        var claim = Assert.Single(TeamClaims(await Service().GetClaimsPrincipal(user.Id, true)));

        Assert.Equal(view.Id, claim.ViewId);
        Assert.Equal(team.Id, claim.TeamId);
        Assert.True(claim.IsPrimary);
        // Observer's three, the team's own UploadViewIsos, and View Member's four.
        Assert.Equal(
            [
                "EditTeam", "UploadTeamIsos", "UploadViewIsos", "UploadVmFiles",
                "ViewNetworks", "ViewTeam", "ViewView"
            ],
            claim.PermissionValues.Order());
    }

    [Fact]
    public async Task Team_claims_expand_an_all_permissions_team_role_to_every_stored_team_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.ViewAdmin);
        await Seed(view, team);
        var user = await SeedMember(view, team);

        var claim = Assert.Single(TeamClaims(await Service().GetClaimsPrincipal(user.Id, true)));

        var stored = await Db.TeamPermissions.Select(x => x.Name).ToArrayAsync(Ct);
        Assert.Equal(stored.Order(), claim.PermissionValues.Order());
    }

    /// <summary>
    /// The membership's role can widen a team beyond its default, which is how one user is made an
    /// administrator of a team everyone else is an ordinary member of.
    /// </summary>
    [Fact]
    public async Task Team_claims_expand_an_all_permissions_membership_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.Observer);
        await Seed(view, team);
        var user = await SeedMember(view, team, membershipRoleId: TestData.TeamRoles.ViewAdmin);

        var claim = Assert.Single(TeamClaims(await Service().GetClaimsPrincipal(user.Id, true)));

        var stored = await Db.TeamPermissions.Select(x => x.Name).ToArrayAsync(Ct);
        Assert.Equal(stored.Order(), claim.PermissionValues.Order());
    }

    /// <summary>
    /// A scope grants the granting team's permissions over a team the user is not in. The target claim
    /// records where the grant came from and leaves its direct permissions empty, which is how the
    /// readers tell a scoped claim from a membership.
    /// </summary>
    [Fact]
    public async Task Team_claims_include_a_scoped_team_with_no_direct_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting", TestData.TeamRoles.Observer);
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);
        var user = await SeedMember(view, team);
        await Seed(TestData.TeamPermissionScope(team.Id, target.Id));

        var claims = TeamClaims(await Service().GetClaimsPrincipal(user.Id, true));

        var scoped = Assert.Single(claims, x => x.TeamId == target.Id);
        Assert.Equal(view.Id, scoped.ViewId);
        Assert.Equal([team.Id], scoped.SourceTeamIds);
        Assert.Empty(scoped.DirectPermissionValues);
        Assert.Equal(["ViewNetworks", "ViewTeam", "ViewView"], scoped.PermissionValues.Order());
        Assert.False(scoped.IsPrimary);
    }

    /// <summary>
    /// One claim per team however many sources contributed, because the requirement handler matches a
    /// single claim per team id — a second claim for the same team would be invisible.
    /// </summary>
    [Fact]
    public async Task Team_claims_merge_a_scope_into_the_targets_own_membership_claim()
    {
        var view = TestData.View();
        var granting = TestData.Team(view.Id, "Granting", TestData.TeamRoles.Observer);
        var target = TestData.Team(view.Id, "Target", TestData.TeamRoles.ViewMember);
        await Seed(view, granting, target);
        var user = await SeedMember(view, granting, target);
        await Seed(TestData.TeamPermissionScope(granting.Id, target.Id));

        var claims = TeamClaims(await Service().GetClaimsPrincipal(user.Id, true));

        var merged = Assert.Single(claims, x => x.TeamId == target.Id);
        Assert.Equal(new[] { granting.Id, target.Id }.Order(), merged.SourceTeamIds.Order());
        Assert.Equal(
            ["EditTeam", "UploadTeamIsos", "UploadVmFiles", "ViewNetworks", "ViewTeam", "ViewView"],
            merged.PermissionValues.Order());

        // Only what the membership itself grants, so a scoped grant cannot widen the direct set.
        Assert.Equal(
            ["EditTeam", "UploadTeamIsos", "UploadVmFiles", "ViewTeam"],
            merged.DirectPermissionValues.Order());
    }

    // ---- The user row -----------------------------------------------------------------------------

    /// <summary>
    /// First sign-in: the identity provider is the source of truth for who exists, so a user is created
    /// on demand from the token.
    /// </summary>
    [Fact]
    public async Task AddUserClaims_creates_a_missing_user_from_the_name_claim()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipalBuilder().WithUserId(id).WithName("New User").Build();

        await Service().AddUserClaims(principal, true);

        using var db = NewContext();
        Assert.Equal("New User", db.Users.Single(x => x.Id == id).Name);
    }

    [Fact]
    public async Task AddUserClaims_names_a_user_without_a_name_claim_Anonymous()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "Test"));

        await Service().AddUserClaims(principal, true);

        using var db = NewContext();
        Assert.Equal("Anonymous", db.Users.Single(x => x.Id == id).Name);
    }

    /// <summary>
    /// A rename in the identity provider follows through on the next request.
    /// </summary>
    [Fact]
    public async Task AddUserClaims_renames_a_user_whose_name_claim_changed()
    {
        var user = TestData.User(name: "Old Name");
        await Seed(user);
        var principal = new ClaimsPrincipalBuilder().WithUserId(user.Id).WithName("New Name").Build();

        await Service().AddUserClaims(principal, true);

        using var db = NewContext();
        Assert.Equal("New Name", db.Users.Single(x => x.Id == user.Id).Name);
    }

    /// <summary>
    /// Characterizes current behaviour. Without <c>update</c> an unknown user leaves the claims entry
    /// null and it is dereferenced immediately, so refreshing a deleted user's claims throws. Flip to an
    /// empty principal once the null is handled.
    /// </summary>
    [Fact]
    public async Task GetClaimsPrincipal_throws_for_a_user_who_does_not_exist()
    {
        await Assert.ThrowsAsync<NullReferenceException>(
            () => Service().GetClaimsPrincipal(Guid.NewGuid(), true));
    }

    // ---- Caching ----------------------------------------------------------------------------------

    /// <summary>
    /// Claims are cached per user, so a permission change mid-session is not seen until the entry
    /// expires or is evicted.
    /// </summary>
    [Fact]
    public async Task Claims_are_served_from_the_cache_when_caching_is_enabled()
    {
        var role = await SeedRole("Cached", SystemPermission.CreateViews);
        var user = TestData.User(roleId: role.Id);
        await Seed(user);
        var service = Service(options => options.EnableCaching = true);

        await service.GetClaimsPrincipal(user.Id, true);
        await GrantPermission(role, SystemPermission.ViewViews);

        var principal = await service.GetClaimsPrincipal(user.Id, true);

        Assert.Equal(["CreateViews"], Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    [Fact]
    public async Task Claims_are_recomputed_every_call_when_caching_is_disabled()
    {
        var role = await SeedRole("Uncached", SystemPermission.CreateViews);
        var user = TestData.User(roleId: role.Id);
        await Seed(user);
        var service = Service();

        await service.GetClaimsPrincipal(user.Id, true);
        await GrantPermission(role, SystemPermission.ViewViews);

        var principal = await service.GetClaimsPrincipal(user.Id, true);

        Assert.Equal(
            ["CreateViews", "ViewViews"],
            Values(principal, AuthorizationConstants.PermissionsClaimType).Order());
    }

    /// <summary>
    /// The eviction the team and membership handlers call after changing what a user may do.
    /// </summary>
    [Fact]
    public async Task RefreshClaims_evicts_the_cached_entry()
    {
        var role = await SeedRole("Refreshed", SystemPermission.CreateViews);
        var user = TestData.User(roleId: role.Id);
        await Seed(user);
        var service = Service(options => options.EnableCaching = true);

        await service.GetClaimsPrincipal(user.Id, true);
        await GrantPermission(role, SystemPermission.ViewViews);

        var principal = await service.RefreshClaims(user.Id);

        Assert.Equal(
            ["CreateViews", "ViewViews"],
            Values(principal, AuthorizationConstants.PermissionsClaimType).Order());
    }

    // ---- Roles from the identity provider ---------------------------------------------------------

    [Fact]
    public async Task Claims_add_the_permissions_of_a_role_named_in_the_token()
    {
        var role = await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = new ClaimsPrincipalBuilder().WithUserId(user.Id).WithClaim("roles", role.Name).Build();
        await Service(FromToken("roles")).AddUserClaims(principal, false);

        Assert.Equal(["CreateViews"], Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    /// <summary>
    /// Role names are matched case-insensitively, since the identity provider's casing is not this
    /// application's to control.
    /// </summary>
    [Fact]
    public async Task Claims_match_a_token_role_name_regardless_of_case()
    {
        var role = await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = new ClaimsPrincipalBuilder().WithUserId(user.Id).WithClaim("roles", "OPS").Build();
        await Service(FromToken("roles")).AddUserClaims(principal, false);

        Assert.Equal(["CreateViews"], Values(principal, AuthorizationConstants.PermissionsClaimType));
        Assert.Equal("Ops", role.Name);
    }

    /// <summary>
    /// The configured path walks into a JSON claim, which is how Keycloak ships roles.
    /// </summary>
    [Fact]
    public async Task Claims_read_token_roles_from_a_nested_json_claim()
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = TokenWithJson(user.Id, """{"roles":["Ops","Unknown"]}""");
        await Service(FromToken("realm_access.roles")).AddUserClaims(principal, false);

        Assert.Equal(["CreateViews"], Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    [Fact]
    public async Task Claims_read_a_single_token_role_from_a_json_string()
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = TokenWithJson(user.Id, """{"roles":"Ops"}""");
        await Service(FromToken("realm_access.roles")).AddUserClaims(principal, false);

        Assert.Equal(["CreateViews"], Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    /// <summary>
    /// A dot escaped with a backslash is part of the claim name rather than a step into the JSON, so a
    /// provider that issues dotted claim names is still usable.
    /// </summary>
    [Fact]
    public async Task Claims_treat_an_escaped_dot_in_the_configured_path_as_a_literal()
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = new ClaimsPrincipalBuilder().WithUserId(user.Id).WithClaim("my.roles", "Ops").Build();
        await Service(FromToken(@"my\.roles")).AddUserClaims(principal, false);

        Assert.Equal(["CreateViews"], Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    /// <summary>
    /// Token roles are opt-in: with the option off the same claim grants nothing, so enabling it is what
    /// hands the identity provider control over system permissions.
    /// </summary>
    [Fact]
    public async Task Claims_ignore_token_roles_unless_the_option_is_on()
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = new ClaimsPrincipalBuilder().WithUserId(user.Id).WithClaim("roles", "Ops").Build();
        await Service(options => options.RolesClaimPath = "roles").AddUserClaims(principal, false);

        Assert.Empty(Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    [Theory]
    // A role the database does not have.
    [InlineData("realm_access.roles", """{"roles":["No Such Role"]}""")]
    // A path that is not in the claim.
    [InlineData("realm_access.groups", """{"roles":["Ops"]}""")]
    // Not a string or an array.
    [InlineData("realm_access.roles", """{"roles":5}""")]
    // Not JSON at all.
    [InlineData("realm_access.roles", "not json")]
    public async Task Claims_add_nothing_when_the_token_roles_do_not_resolve(string path, string json)
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = TokenWithJson(user.Id, json);
        await Service(FromToken(path)).AddUserClaims(principal, false);

        Assert.Empty(Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    /// <summary>
    /// No configured path means no token roles to read, which is the setting's off position.
    /// </summary>
    [Fact]
    public async Task Claims_add_nothing_when_no_roles_claim_path_is_configured()
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var principal = TokenWithJson(user.Id, """{"roles":["Ops"]}""");
        await Service(FromToken("")).AddUserClaims(principal, false);

        Assert.Empty(Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    /// <summary>
    /// Cached claims are dropped when a new token carries different roles, so a role revoked in the
    /// identity provider takes effect on the next token rather than after the cache expires.
    /// </summary>
    [Fact]
    public async Task Claims_are_recomputed_for_a_new_token_whose_roles_changed()
    {
        await SeedRole("Ops", SystemPermission.CreateViews);
        await SeedRole("Auditor", SystemPermission.ViewViews);
        var user = TestData.User();
        await Seed(user);

        var service = Service(options =>
        {
            options.EnableCaching = true;
            options.UseRolesFromIdP = true;
            options.RolesClaimPath = "roles";
        });

        await service.AddUserClaims(TokenWithRole(user.Id, "token-1", "Ops"), false);

        var second = TokenWithRole(user.Id, "token-2", "Auditor");
        await service.AddUserClaims(second, false);

        Assert.Equal(["ViewViews"], Values(second, AuthorizationConstants.PermissionsClaimType));
    }

    /// <summary>
    /// A new token with the same roles reuses the cached entry — the check is on what the token grants,
    /// not on the token being new.
    /// </summary>
    [Fact]
    public async Task Claims_reuse_the_cache_for_a_new_token_whose_roles_are_unchanged()
    {
        var role = await SeedRole("Ops", SystemPermission.CreateViews);
        var user = TestData.User();
        await Seed(user);

        var service = Service(options =>
        {
            options.EnableCaching = true;
            options.UseRolesFromIdP = true;
            options.RolesClaimPath = "roles";
        });

        await service.AddUserClaims(TokenWithRole(user.Id, "token-1", "Ops"), false);
        await GrantPermission(role, SystemPermission.ViewViews);

        var second = TokenWithRole(user.Id, "token-2", "Ops");
        await service.AddUserClaims(second, false);

        Assert.Equal(["CreateViews"], Values(second, AuthorizationConstants.PermissionsClaimType));
    }

    // ---- Adding to an identity --------------------------------------------------------------------

    /// <summary>
    /// Characterizes current behaviour. Claims are skipped by type rather than by type and value, so an
    /// identity that already carries one permission claim receives none of the computed ones. A token
    /// issued with its own <c>Permission</c> claim therefore loses every permission the database grants.
    /// </summary>
    [Fact]
    public async Task AddUserClaims_skips_every_claim_of_a_type_the_identity_already_has()
    {
        var role = await SeedRole("Ops", SystemPermission.CreateViews, SystemPermission.ViewViews);
        var user = TestData.User(roleId: role.Id);
        await Seed(user);

        var principal = new ClaimsPrincipalBuilder()
            .WithUserId(user.Id)
            .WithRawSystemPermission("SomethingElse")
            .Build();

        await Service().AddUserClaims(principal, false);

        Assert.Equal(["SomethingElse"], Values(principal, AuthorizationConstants.PermissionsClaimType));
    }

    // ---- The current principal --------------------------------------------------------------------

    [Fact]
    public void SetCurrentClaimsPrincipal_is_what_GetCurrentClaimsPrincipal_returns()
    {
        var service = Service();
        var principal = ClaimsPrincipalBuilder.Anonymous();

        service.SetCurrentClaimsPrincipal(principal);

        Assert.Same(principal, service.GetCurrentClaimsPrincipal());
    }

    [Fact]
    public async Task GetClaimsPrincipal_replaces_the_current_principal_when_asked()
    {
        var user = TestData.User();
        await Seed(user);
        var service = Service();

        var principal = await service.GetClaimsPrincipal(user.Id, true);

        Assert.Same(principal, service.GetCurrentClaimsPrincipal());
    }

    /// <summary>
    /// A refresh for somebody else leaves the caller's own principal alone — the handlers that call this
    /// are acting on another user.
    /// </summary>
    [Fact]
    public async Task GetClaimsPrincipal_leaves_the_current_principal_alone_for_another_user()
    {
        var user = TestData.User();
        await Seed(user);
        var service = Service();
        var current = ClaimsPrincipalBuilder.Anonymous();
        service.SetCurrentClaimsPrincipal(current);

        await service.GetClaimsPrincipal(user.Id, false);

        Assert.Same(current, service.GetCurrentClaimsPrincipal());
    }

    /// <summary>
    /// A refresh for the current user updates it in place, so the rest of the request sees the new
    /// permissions.
    /// </summary>
    [Fact]
    public async Task GetClaimsPrincipal_updates_the_current_principal_for_the_same_user()
    {
        var user = TestData.User();
        await Seed(user);
        var service = Service();
        service.SetCurrentClaimsPrincipal(new ClaimsPrincipalBuilder().WithUserId(user.Id).Build());

        var principal = await service.GetClaimsPrincipal(user.Id, false);

        Assert.Same(principal, service.GetCurrentClaimsPrincipal());
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// The service over a host of its own, so one test's <paramref name="configure"/> — and its claims
    /// cache — is never another's. Hosts are keyed by principal, and this builds a fresh one.
    /// </summary>
    private IUserClaimsService Service(Action<ClaimsTransformationOptions> configure = null) =>
        HostFor(new ClaimsPrincipalBuilder().Build(), options => configure?.Invoke(options.ClaimsTransformation))
            .Resolve<IUserClaimsService>();

    /// <summary>Turns on token roles at <paramref name="path"/>.</summary>
    private static Action<ClaimsTransformationOptions> FromToken(string path) =>
        options =>
        {
            options.UseRolesFromIdP = true;
            options.RolesClaimPath = path;
        };

    /// <summary>A token carrying <paramref name="json"/> as a <c>realm_access</c> JSON claim.</summary>
    private static ClaimsPrincipal TokenWithJson(Guid userId, string json) =>
        new ClaimsPrincipalBuilder()
            .WithUserId(userId)
            .WithClaim("realm_access", json, JsonClaimValueTypes.Json)
            .Build();

    private static ClaimsPrincipal TokenWithRole(Guid userId, string tokenId, string role) =>
        new ClaimsPrincipalBuilder()
            .WithUserId(userId)
            .WithClaim(JwtRegisteredClaimNames.Jti, tokenId)
            .WithClaim("roles", role)
            .Build();

    /// <summary>A role granting <paramref name="permissions"/>, resolved from the seeded rows.</summary>
    private async Task<RoleEntity> SeedRole(string name, params SystemPermission[] permissions)
    {
        var role = new RoleEntity { Id = Guid.NewGuid(), Name = name };
        await Seed(role);

        foreach (var permission in permissions)
        {
            await GrantPermission(role, permission);
        }

        return role;
    }

    private async Task GrantPermission(RoleEntity role, SystemPermission permission)
    {
        var id = await Db.Permissions
            .Where(x => x.Name == permission.ToString())
            .Select(x => x.Id)
            .SingleAsync(Ct);

        await Seed(new RolePermissionEntity(role.Id, id));
    }

    /// <summary>
    /// A user in every one of <paramref name="teams"/>, with the first as their primary team.
    /// </summary>
    private async Task<UserEntity> SeedMember(
        ViewEntity view,
        TeamEntity firstTeam,
        params TeamEntity[] otherTeams)
    {
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var primary = TestData.TeamMembership(firstTeam.Id, user.Id, viewMembership.Id);
        await Seed(user, viewMembership, primary);
        await Seed([.. otherTeams.Select(x => TestData.TeamMembership(x.Id, user.Id, viewMembership.Id))]);

        viewMembership.PrimaryTeamMembershipId = primary.Id;
        await Db.SaveChangesAsync(Ct);

        return user;
    }

    /// <summary>
    /// A user in <paramref name="team"/> whose membership carries <paramref name="membershipRoleId"/> —
    /// the role that widens one member beyond the team's default.
    /// </summary>
    private async Task<UserEntity> SeedMember(ViewEntity view, TeamEntity team, Guid membershipRoleId)
    {
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var membership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id, membershipRoleId);
        await Seed(user, viewMembership, membership);

        viewMembership.PrimaryTeamMembershipId = membership.Id;
        await Db.SaveChangesAsync(Ct);

        return user;
    }

    private static string[] Values(ClaimsPrincipal principal, string type) =>
        [.. principal.Claims.Where(x => x.Type == type).Select(x => x.Value)];

    private static TeamPermissionsClaim[] TeamClaims(ClaimsPrincipal principal) =>
        [.. Values(principal, AuthorizationConstants.TeamPermissionsClaimType)
            .Select(TeamPermissionsClaim.FromString)];
}
