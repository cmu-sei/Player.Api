// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Crucible.Common.EntityEvents.Events;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Roles.EventHandlers;
using Player.Api.Features.TeamMemberships.EventHandlers;
using Player.Api.Features.TeamPermissionScopes.EventHandlers;
using Player.Api.Features.TeamRoles.EventHandlers;
using Player.Api.Features.Teams.EventHandlers;
using Player.Api.Features.Users.EventHandlers;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features;

/// <summary>
/// The handlers that drop a user's cached claims when something changes what those claims would be.
/// </summary>
/// <remarks>
/// <para>
/// <c>UserClaimsService</c> caches a user's resolved permissions under their user id for
/// <c>CacheExpirationSeconds</c>. Nothing recomputes them in between, so until the entry is evicted the
/// user keeps the permissions they had — a revoked role stays in force. These fourteen handlers are the
/// only thing that closes that window, and each one has to work out for itself which users a change
/// reaches: a role change reaches everyone holding the role, a team change everyone on the team.
/// </para>
/// <para>
/// They live in six feature folders but implement one rule, so they are tested together — the interesting
/// question is which changes evict whom, and that only reads as a set. Cached values here are placeholder
/// strings; only presence matters, and the handlers never look at the value.
/// </para>
/// </remarks>
public class AuthCacheEvictionTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    // ---- Users ------------------------------------------------------------------------------------

    /// <summary>
    /// A user's role is where their system permissions come from, so a reassignment has to take effect
    /// without waiting out the cache.
    /// </summary>
    [Fact]
    public async Task Giving_a_user_a_different_role_evicts_that_user()
    {
        var user = TestData.User();
        Cache(user.Id);

        await new UserUpdatedAuthCacheHandler(_cache).Handle(
            Updated(user, nameof(UserEntity.RoleId)), Ct);

        Assert.False(IsCached(user.Id));
    }

    /// <summary>
    /// Renaming a user cannot change their permissions, so the cache stands — the point of checking the
    /// modified properties at all is that every save on a user would otherwise force a re-resolve.
    /// </summary>
    [Fact]
    public async Task Changing_something_other_than_a_users_role_leaves_the_cache_alone()
    {
        var user = TestData.User();
        Cache(user.Id);

        await new UserUpdatedAuthCacheHandler(_cache).Handle(
            Updated(user, nameof(UserEntity.Name)), Ct);

        Assert.True(IsCached(user.Id));
    }

    [Fact]
    public async Task Deleting_a_user_evicts_that_user()
    {
        var user = TestData.User();
        Cache(user.Id);

        await new UserDeletedAuthCacheHandler(_cache).Handle(new EntityDeleted<UserEntity>(user), Ct);

        Assert.False(IsCached(user.Id));
    }

    // ---- Team memberships -------------------------------------------------------------------------

    /// <summary>
    /// Membership is what carries a user's team permissions, so all three of joining, changing team role,
    /// and leaving invalidate the claim — no modified-property check, because a membership row has nothing
    /// on it that does not matter.
    /// </summary>
    [Fact]
    public async Task Adding_a_user_to_a_team_evicts_that_user()
    {
        var membership = TestData.TeamMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Cache(membership.UserId);

        await new TeamMembershipCreatedAuthCacheHandler(_cache).Handle(
            new EntityCreated<TeamMembershipEntity>(membership), Ct);

        Assert.False(IsCached(membership.UserId));
    }

    [Fact]
    public async Task Changing_a_team_membership_evicts_that_user()
    {
        var membership = TestData.TeamMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Cache(membership.UserId);

        await new TeamMembershipUpdatedAuthCacheHandler(_cache).Handle(
            Updated(membership, nameof(TeamMembershipEntity.RoleId)), Ct);

        Assert.False(IsCached(membership.UserId));
    }

    [Fact]
    public async Task Removing_a_user_from_a_team_evicts_that_user()
    {
        var membership = TestData.TeamMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Cache(membership.UserId);

        await new TeamMembershipDeletedAuthCacheHandler(_cache).Handle(
            new EntityDeleted<TeamMembershipEntity>(membership), Ct);

        Assert.False(IsCached(membership.UserId));
    }

    // ---- Roles ------------------------------------------------------------------------------------

    /// <summary>
    /// A role change fans out to every user holding it, and to nobody else — the eviction is a query, not
    /// a flush, so an unrelated user's cached claims survive.
    /// </summary>
    [Fact]
    public async Task Making_a_role_all_permissions_evicts_everyone_holding_it()
    {
        var (role, holder, other) = await SeedRole();

        await new RoleUpdatedAuthCacheHandler(_cache, Db).Handle(
            Updated(role, nameof(RoleEntity.AllPermissions)), Ct);

        Assert.False(IsCached(holder.Id));
        Assert.True(IsCached(other.Id));
    }

    [Fact]
    public async Task Renaming_a_role_leaves_the_cache_alone()
    {
        var (role, holder, _) = await SeedRole();

        await new RoleUpdatedAuthCacheHandler(_cache, Db).Handle(
            Updated(role, nameof(RoleEntity.Name)), Ct);

        Assert.True(IsCached(holder.Id));
    }

    [Fact]
    public async Task Deleting_a_role_evicts_everyone_holding_it()
    {
        var (role, holder, other) = await SeedRole();

        await new RoleDeletedAuthCacheHandler(_cache, Db).Handle(new EntityDeleted<RoleEntity>(role), Ct);

        Assert.False(IsCached(holder.Id));
        Assert.True(IsCached(other.Id));
    }

    /// <summary>
    /// Granting or revoking one permission on a role is the common case, and it arrives as a change to the
    /// join row rather than to the role — so these handlers read the role id off the join.
    /// </summary>
    [Fact]
    public async Task Granting_a_role_a_permission_evicts_everyone_holding_it()
    {
        var (role, holder, other) = await SeedRole();

        await new RolePermissionCreatedAuthCacheHandler(_cache, Db).Handle(
            new EntityCreated<RolePermissionEntity>(new RolePermissionEntity { RoleId = role.Id }), Ct);

        Assert.False(IsCached(holder.Id));
        Assert.True(IsCached(other.Id));
    }

    [Fact]
    public async Task Revoking_a_roles_permission_evicts_everyone_holding_it()
    {
        var (role, holder, _) = await SeedRole();

        await new RolePermissioDeletedAuthCacheHandler(_cache, Db).Handle(
            new EntityDeleted<RolePermissionEntity>(new RolePermissionEntity { RoleId = role.Id }), Ct);

        Assert.False(IsCached(holder.Id));
    }

    // ---- Team roles -------------------------------------------------------------------------------

    /// <summary>
    /// A team role is held per membership rather than per user, so the fan-out is over memberships — the
    /// same user on two teams with different roles is only evicted by the role they actually hold.
    /// </summary>
    [Fact]
    public async Task Making_a_team_role_all_permissions_evicts_everyone_holding_it()
    {
        var world = await SeedTeams();

        await new TeamRoleUpdatedAuthCacheHandler(_cache, Db).Handle(
            Updated(world.TeamRole, nameof(TeamRoleEntity.AllPermissions)), Ct);

        Assert.False(IsCached(world.Member.Id));
        Assert.True(IsCached(world.OtherTeamMember.Id));
    }

    [Fact]
    public async Task Renaming_a_team_role_leaves_the_cache_alone()
    {
        var world = await SeedTeams();

        await new TeamRoleUpdatedAuthCacheHandler(_cache, Db).Handle(
            Updated(world.TeamRole, nameof(TeamRoleEntity.Name)), Ct);

        Assert.True(IsCached(world.Member.Id));
    }

    [Fact]
    public async Task Deleting_a_team_role_evicts_everyone_holding_it()
    {
        var world = await SeedTeams();

        await new TeamRoleDeletedAuthCacheHandler(_cache, Db).Handle(
            new EntityDeleted<TeamRoleEntity>(world.TeamRole), Ct);

        Assert.False(IsCached(world.Member.Id));
        Assert.True(IsCached(world.OtherTeamMember.Id));
    }

    [Fact]
    public async Task Granting_a_team_role_a_permission_evicts_everyone_holding_it()
    {
        var world = await SeedTeams();

        await new TeamRolePermissionCreatedAuthCacheHandler(_cache, Db).Handle(
            new EntityCreated<TeamRolePermissionEntity>(
                new TeamRolePermissionEntity { RoleId = world.TeamRole.Id }), Ct);

        Assert.False(IsCached(world.Member.Id));
    }

    [Fact]
    public async Task Revoking_a_team_roles_permission_evicts_everyone_holding_it()
    {
        var world = await SeedTeams();

        await new TeamRolePermissioDeletedAuthCacheHandler(_cache, Db).Handle(
            new EntityDeleted<TeamRolePermissionEntity>(
                new TeamRolePermissionEntity { RoleId = world.TeamRole.Id }), Ct);

        Assert.False(IsCached(world.Member.Id));
    }

    // ---- Teams ------------------------------------------------------------------------------------

    /// <summary>
    /// Moving a team to a different team role changes every member's permissions at once, which is why the
    /// team handler exists alongside the membership one.
    /// </summary>
    [Fact]
    public async Task Giving_a_team_a_different_role_evicts_its_members()
    {
        var world = await SeedTeams();

        await new TeamUpdatedAuthCacheHandler(_cache, Db).Handle(
            Updated(world.Team, nameof(TeamEntity.RoleId)), Ct);

        Assert.False(IsCached(world.Member.Id));
        Assert.True(IsCached(world.OtherTeamMember.Id));
    }

    [Fact]
    public async Task Renaming_a_team_leaves_the_cache_alone()
    {
        var world = await SeedTeams();

        await new TeamUpdatedAuthCacheHandler(_cache, Db).Handle(
            Updated(world.Team, nameof(TeamEntity.Name)), Ct);

        Assert.True(IsCached(world.Member.Id));
    }

    /// <summary>
    /// A permission assigned straight to the team, bypassing its role — the other way a team's members can
    /// gain or lose a permission.
    /// </summary>
    [Fact]
    public async Task Assigning_a_permission_to_a_team_evicts_its_members()
    {
        var world = await SeedTeams();

        await new TeamPermissionAssignmentCreatedAuthCacheHandler(_cache, Db).Handle(
            new EntityCreated<TeamPermissionAssignmentEntity>(
                new TeamPermissionAssignmentEntity { TeamId = world.Team.Id }), Ct);

        Assert.False(IsCached(world.Member.Id));
        Assert.True(IsCached(world.OtherTeamMember.Id));
    }

    [Fact]
    public async Task Removing_a_permission_from_a_team_evicts_its_members()
    {
        var world = await SeedTeams();

        await new TeamPermissionAssignmentDeletedAuthCacheHandler(_cache, Db).Handle(
            new EntityDeleted<TeamPermissionAssignmentEntity>(
                new TeamPermissionAssignmentEntity { TeamId = world.Team.Id }), Ct);

        Assert.False(IsCached(world.Member.Id));
    }

    // ---- Team permission scopes -------------------------------------------------------------------

    /// <summary>
    /// A scope widens which teams the scoping team's permissions apply to, so it is the scoping team's
    /// members whose claims change — not the target team's.
    /// </summary>
    [Fact]
    public async Task Scoping_a_teams_permissions_onto_another_team_evicts_the_scoping_teams_members()
    {
        var world = await SeedTeams();
        var scope = TestData.TeamPermissionScope(world.Team.Id, world.OtherTeam.Id);

        await new TeamPermissionScopeCreatedAuthCacheHandler(_cache, Db).Handle(
            new EntityCreated<TeamPermissionScopeEntity>(scope), Ct);

        Assert.False(IsCached(world.Member.Id));
        Assert.True(IsCached(world.OtherTeamMember.Id));
    }

    [Fact]
    public async Task Removing_a_scope_evicts_the_scoping_teams_members()
    {
        var world = await SeedTeams();
        var scope = TestData.TeamPermissionScope(world.Team.Id, world.OtherTeam.Id);

        await new TeamPermissionScopeDeletedAuthCacheHandler(_cache, Db).Handle(
            new EntityDeleted<TeamPermissionScopeEntity>(scope), Ct);

        Assert.False(IsCached(world.Member.Id));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private void Cache(params Guid[] userIds)
    {
        foreach (var userId in userIds)
        {
            _cache.Set(userId, "resolved claims");
        }
    }

    private bool IsCached(Guid userId) => _cache.TryGetValue(userId, out _);

    private static EntityUpdated<T> Updated<T>(T entity, params string[] modifiedProperties) =>
        new(entity, modifiedProperties);

    /// <summary>One role held by one of two users, both of whom start with cached claims.</summary>
    private async Task<(RoleEntity Role, UserEntity Holder, UserEntity Other)> SeedRole()
    {
        // Not one of the roles the context seeds — role names are unique.
        var role = new RoleEntity { Id = Guid.NewGuid(), Name = "Exercise Author" };
        var holder = TestData.User(name: "Holder", roleId: role.Id);
        var other = TestData.User(name: "Other");
        await Seed(role, holder, other);

        Cache(holder.Id, other.Id);
        return (role, holder, other);
    }

    /// <summary>
    /// Two teams in one view, each with one member, only the first on <c>TeamRole</c> — enough to tell
    /// "everyone affected" from "everyone".
    /// </summary>
    private async Task<TeamWorld> SeedTeams()
    {
        var view = TestData.View();
        var teamRole = new TeamRoleEntity { Id = Guid.NewGuid(), Name = "Team Lead" };
        var team = TestData.Team(view.Id, "Blue Team", teamRole.Id);
        var otherTeam = TestData.Team(view.Id, "Red Team");

        var member = TestData.User(name: "Blue Member");
        var otherMember = TestData.User(name: "Red Member");
        var membership = TestData.ViewMembership(view.Id, member.Id);
        var otherMembership = TestData.ViewMembership(view.Id, otherMember.Id);

        await Seed(
            view, teamRole, team, otherTeam, member, otherMember, membership, otherMembership,
            TestData.TeamMembership(team.Id, member.Id, membership.Id, teamRole.Id),
            TestData.TeamMembership(otherTeam.Id, otherMember.Id, otherMembership.Id));

        Cache(member.Id, otherMember.Id);
        return new TeamWorld(view, teamRole, team, otherTeam, member, otherMember);
    }

    private sealed record TeamWorld(
        ViewEntity View,
        TeamRoleEntity TeamRole,
        TeamEntity Team,
        TeamEntity OtherTeam,
        UserEntity Member,
        UserEntity OtherTeamMember);

    public override async ValueTask DisposeAsync()
    {
        _cache.Dispose();
        await base.DisposeAsync();
    }
}
