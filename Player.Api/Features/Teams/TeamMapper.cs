// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Features.TeamPermissions;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Teams;

/// <summary>
/// The Teams feature's mappings. Mapperly generates the partial methods at compile time; see
/// <c>Infrastructure/Mappings/MapperDefaults.cs</c> for the conventions every mapper shares.
/// </summary>
[Mapper]
public static partial class TeamMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    /// <summary>
    /// Projects teams inside the database query, so only the columns <see cref="Team"/> needs are read.
    /// </summary>
    /// <remarks>
    /// <see cref="Team.IsMember"/> and <see cref="Team.IsPrimary"/> describe the caller rather than the
    /// team, so the projection leaves them false. Set them with <see cref="WithCallerFlags(Team, ClaimsPrincipal)"/>.
    /// </remarks>
    public static partial IQueryable<Team> ProjectToTeam(this IQueryable<TeamEntity> query);

    /// <summary>
    /// The member configuration for <see cref="ProjectToTeam"/>, which Mapperly inlines into the
    /// projection's expression tree.
    /// </summary>
    [MapProperty(nameof(@TeamEntity.Role.Name), nameof(Team.RoleName))]
    [MapProperty(nameof(TeamEntity.Permissions), nameof(Team.Permissions), Use = nameof(MapAssignedPermissions))]
    [MapProperty(nameof(TeamEntity.Scopes), nameof(Team.ScopedTeamIds), Use = nameof(MapScopedTeamIds))]
    [MapperIgnoreTarget(nameof(Team.IsMember))]
    [MapperIgnoreTarget(nameof(Team.IsPrimary))]
    private static partial Team MapToTeam(TeamEntity source);

    // Expression-bodied, so Mapperly can inline them into the projection rather than call them.
    private static List<TeamPermissionModel> MapAssignedPermissions(ICollection<TeamPermissionAssignmentEntity> assignments) =>
        assignments.Select(x => MapToTeamPermissionModel(x.Permission)).ToList();

    private static List<Guid> MapScopedTeamIds(ICollection<TeamPermissionScopeEntity> scopes) =>
        scopes.Select(x => x.TargetTeamId).ToList();

    private static partial TeamPermissionModel MapToTeamPermissionModel(TeamPermissionEntity source);

    /// <summary>
    /// Sets <see cref="Team.IsMember"/> and <see cref="Team.IsPrimary"/> from <paramref name="user"/>'s
    /// claims and returns the same team.
    /// </summary>
    public static Team WithCallerFlags(this Team team, ClaimsPrincipal user)
    {
        if (team == null)
            return null;

        team.IsMember = user.IsTeamMember(team.Id);
        team.IsPrimary = user.IsPrimaryTeam(team.Id);
        return team;
    }

    /// <inheritdoc cref="WithCallerFlags(Team, ClaimsPrincipal)"/>
    public static Team[] WithCallerFlags(this IEnumerable<Team> teams, ClaimsPrincipal user) =>
        [.. teams.Select(x => x.WithCallerFlags(user))];

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>
    /// A new entity from the create command. A null <see cref="Create.Command.RoleId"/> leaves the
    /// entity's default for the handler to fill.
    /// </summary>
    /// <remarks>
    /// The id and the view come from the route and the caller's choice of id, which the handler
    /// resolves, so the handler sets both keys and the mapper sets neither.
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Create.Command.Id))]
    [MapperIgnoreSource(nameof(Create.Command.ViewId))]
    public static partial TeamEntity ToEntity(this Create.Command source);

    /// <summary>
    /// Applies the edit command to an existing entity. A null <see cref="Edit.Command.RoleId"/>
    /// keeps the team's current role.
    /// </summary>
    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, TeamEntity target);
}
