// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;

namespace Player.Api.Tests.Support;

/// <summary>
/// A seeded user, and the ids of the rows seeded with them.
/// </summary>
/// <remarks>
/// An HTTP test acts as an actor rather than as a hand-built principal:
/// <c>ApiTestBase.Client(actor)</c> puts the id on the request and the real
/// <c>AuthorizationClaimsTransformer</c> derives the permission claims from these rows. What the actor
/// may do is therefore a property of the database, as it is in production.
/// </remarks>
public sealed class TestActor
{
    public required Guid Id { get; init; }

    /// <summary>
    /// Sent as the <c>name</c> claim, which <c>UserClaimsService.ValidateUser</c> writes back to the
    /// user row and <c>NotificationService</c> reads with <c>Claims.Single</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>One per <see cref="TestActorBuilder.OnTeam"/> call, in the order they were declared.</summary>
    public required IReadOnlyList<TestActorMembership> Memberships { get; init; }

    /// <summary>The first declared membership — the common case of an actor on one team.</summary>
    public TestActorMembership Membership => Memberships.Count > 0
        ? Memberships[0]
        : throw new InvalidOperationException($"Actor {Id} is on no team.");

    /// <summary>The membership on <paramref name="teamId"/>.</summary>
    public TestActorMembership On(Guid teamId) =>
        Memberships.SingleOrDefault(x => x.TeamId == teamId)
        ?? throw new InvalidOperationException($"Actor {Id} has no membership on team {teamId}.");
}

/// <summary>
/// One seeded team membership. The ids are here because requests take them as route values and
/// responses carry them back.
/// </summary>
public sealed record TestActorMembership(
    Guid ViewId,
    Guid TeamId,
    Guid ViewMembershipId,
    Guid TeamMembershipId,
    bool IsPrimary);

/// <summary>
/// Seeds a user, the role that grants their system permissions, and their view and team memberships.
/// </summary>
/// <remarks>
/// <para>
/// This is the HTTP counterpart of <see cref="ClaimsPrincipalBuilder"/>: where that one asserts the
/// claim shapes the transformer would have produced, this one seeds the rows the transformer reads.
/// The mapping is <c>UserClaimsService.GetPermissionClaims</c> — a system permission comes from the
/// user's <see cref="RoleEntity"/>, and a team permission from the union of the team's direct
/// assignments, the team's own role, and the membership role.
/// </para>
/// <para>
/// Roles are minted per actor rather than shared, because role names are uniquely indexed and the
/// seeded rows already hold the obvious names. Where a seeded role says what a test means —
/// <c>TestData.Roles.Administrator</c>, <c>TestData.TeamRoles.ViewAdmin</c> — pass its id instead.
/// </para>
/// </remarks>
public sealed class TestActorBuilder(PlayerContext db, CancellationToken ct)
{
    private readonly List<PendingMembership> _memberships = [];
    private Guid _id = Guid.NewGuid();
    private string _name = "Test Actor";
    private Guid? _roleId;
    private SystemPermission[] _systemPermissions;

    /// <summary>
    /// Fixes the actor's id, for a test that needs to know it before seeding — a request whose route
    /// or body names the user, or an assertion on a stored <c>CreatedBy</c>.
    /// </summary>
    public TestActorBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public TestActorBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// Gives the actor an existing system role, such as <c>TestData.Roles.Administrator</c>.
    /// </summary>
    public TestActorBuilder WithRole(Guid roleId)
    {
        if (_systemPermissions is not null)
        {
            throw new InvalidOperationException(
                "WithRole and WithSystemPermissions both decide the actor's system role. Pass the " +
                "permissions to a role of your own and use WithRole, or drop one of the calls.");
        }

        _roleId = roleId;
        return this;
    }

    /// <summary>
    /// Every system permission, by way of the seeded <c>Administrator</c> role.
    /// </summary>
    /// <remarks>
    /// That role has <c>AllPermissions</c>, so the claims are every row in <c>Permissions</c> — the
    /// twelve <see cref="SystemPermission"/> values and the three the Vm API reads. A superset of
    /// naming the enum's values, and one row rather than thirteen.
    /// </remarks>
    public TestActorBuilder WithAllSystemPermissions() => WithRole(TestData.Roles.Administrator);

    /// <summary>
    /// Exactly these system permissions, by way of a role minted for this actor.
    /// </summary>
    public TestActorBuilder WithSystemPermissions(params SystemPermission[] permissions)
    {
        if (_roleId is not null)
        {
            throw new InvalidOperationException(
                "WithSystemPermissions and WithRole both decide the actor's system role. Drop one.");
        }

        _systemPermissions = permissions;
        return this;
    }

    /// <summary>
    /// Puts the actor on <paramref name="team"/>. Passing <paramref name="viewPermissions"/> or
    /// <paramref name="teamPermissions"/> mints a team role for this membership alone;
    /// <paramref name="roleId"/> names an existing one instead. With neither, the membership carries no
    /// role and the actor's permissions on the team are whatever the team itself grants.
    /// </summary>
    /// <remarks>
    /// A membership role adds to what the team grants, so a test that wants exactly the permissions it
    /// passes needs a team whose own role grants nothing — <c>TestData.Team(view.Id, roleId:
    /// permissionFreeRole.Id)</c>. <c>TestData.Team</c> defaults to the seeded <c>View Member</c> role,
    /// which grants four.
    /// </remarks>
    public TestActorBuilder OnTeam(
        TeamEntity team,
        bool primary = false,
        ViewPermission[] viewPermissions = null,
        TeamPermission[] teamPermissions = null,
        Guid? roleId = null)
    {
        ArgumentNullException.ThrowIfNull(team);

        if (team.ViewId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Team {team.Id} has no ViewId. A membership is scoped to a view, so the team needs one.");
        }

        if (roleId is not null && (viewPermissions is not null || teamPermissions is not null))
        {
            throw new InvalidOperationException(
                $"The membership on team {team.Id} names a role and also asks for permissions, which " +
                "would mint a second role. Pass one or the other.");
        }

        _memberships.Add(new PendingMembership(
            team.ViewId, team.Id, primary, roleId, viewPermissions, teamPermissions));

        return this;
    }

    /// <summary>
    /// Writes the actor and everything above to the database.
    /// </summary>
    /// <remarks>
    /// The views and teams passed to <see cref="OnTeam"/> must already be saved — memberships are
    /// foreign keys onto them.
    /// </remarks>
    public async Task<TestActor> SeedAsync()
    {
        db.Users.Add(TestData.User(_id, _name, await ResolveRoleAsync()));

        var seeded = new Dictionary<Guid, TestActorMembership>();
        List<(ViewMembershipEntity ViewMembership, TeamMembershipEntity Primary)> primaries = [];

        foreach (var view in _memberships.GroupBy(x => x.ViewId))
        {
            var viewMembership = TestData.ViewMembership(view.Key, _id);
            db.ViewMemberships.Add(viewMembership);

            List<(PendingMembership Pending, TeamMembershipEntity Entity)> teams = [];

            foreach (var pending in view)
            {
                var teamMembership = TestData.TeamMembership(
                    pending.TeamId, _id, viewMembership.Id, await ResolveTeamRoleAsync(pending));

                db.TeamMemberships.Add(teamMembership);
                teams.Add((pending, teamMembership));
            }

            var primary = teams.Where(x => x.Pending.Primary).ToArray();

            if (primary.Length > 1)
            {
                throw new InvalidOperationException(
                    $"View {view.Key} has {primary.Length} memberships marked primary, and a view " +
                    "membership has one primary team.");
            }

            // With none marked, the first team declared in the view is the primary one — what
            // Users/AddToTeam does for the first team a user joins in a view. A test that says nothing
            // about primacy then gets the arrangement a sequence of requests would have left.
            var primaryMembership = primary.Length == 1 ? primary[0].Entity : teams[0].Entity;
            primaries.Add((viewMembership, primaryMembership));

            foreach (var (pending, entity) in teams)
            {
                seeded.Add(pending.TeamId, new TestActorMembership(
                    view.Key,
                    pending.TeamId,
                    viewMembership.Id,
                    entity.Id,
                    entity.Id == primaryMembership.Id));
            }
        }

        await db.SaveChangesAsync(ct);

        // A second save, because a view membership names its primary team membership and that membership
        // names the view membership — neither insert can carry the other's key. Users/AddToTeam does the
        // same two saves for the same reason.
        foreach (var (viewMembership, primary) in primaries)
        {
            viewMembership.PrimaryTeamMembershipId = primary.Id;
        }

        if (primaries.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new TestActor
        {
            Id = _id,
            Name = _name,
            Memberships = [.. _memberships.Select(x => seeded[x.TeamId])]
        };
    }

    private async Task<Guid?> ResolveRoleAsync()
    {
        if (_systemPermissions is null)
        {
            return _roleId;
        }

        var role = TestData.Role();
        db.Roles.Add(role);

        string[] names = [.. _systemPermissions.Select(x => x.ToString())];

        var found = await db.Permissions
            .Where(x => names.Contains(x.Name))
            .Select(x => new { x.Id, x.Name })
            .ToArrayAsync(ct);

        RequireAll(names, found.Select(x => x.Name), nameof(PlayerContext.Permissions));

        foreach (var permissionId in found.Select(x => x.Id))
        {
            db.RolePermissions.Add(new RolePermissionEntity(role.Id, permissionId) { Id = Guid.NewGuid() });
        }

        return role.Id;
    }

    private async Task<Guid?> ResolveTeamRoleAsync(PendingMembership pending)
    {
        if (pending.ViewPermissions is null && pending.TeamPermissions is null)
        {
            return pending.RoleId;
        }

        var role = TestData.TeamRole();
        db.TeamRoles.Add(role);

        // Both enums name rows in the one TeamPermissions table — a view permission is a permission a
        // team holds over its view.
        string[] names =
        [
            .. (pending.ViewPermissions ?? []).Select(x => x.ToString()),
            .. (pending.TeamPermissions ?? []).Select(x => x.ToString())
        ];

        var found = await db.TeamPermissions
            .Where(x => names.Contains(x.Name))
            .Select(x => new { x.Id, x.Name })
            .ToArrayAsync(ct);

        RequireAll(names, found.Select(x => x.Name), nameof(PlayerContext.TeamPermissions));

        foreach (var permissionId in found.Select(x => x.Id))
        {
            db.TeamRolePermissions.Add(
                new TeamRolePermissionEntity(role.Id, permissionId) { Id = Guid.NewGuid() });
        }

        return role.Id;
    }

    /// <summary>
    /// Fails naming any of <paramref name="names"/> that <paramref name="table"/> has no row for — a
    /// permission with no row would otherwise grant nothing, silently.
    /// </summary>
    private static void RequireAll(string[] names, IEnumerable<string> found, string table)
    {
        var missing = names.Distinct().Except(found).ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"{table} has no row named {string.Join(", ", missing)}, so nothing would grant it. " +
                "The seed data names every enum value; a renamed value or a stale migration is the " +
                "usual cause.");
        }
    }

    private sealed record PendingMembership(
        Guid ViewId,
        Guid TeamId,
        bool Primary,
        Guid? RoleId,
        ViewPermission[] ViewPermissions,
        TeamPermission[] TeamPermissions);
}
