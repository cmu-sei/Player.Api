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
    /// Projects teams inside the database query, so only the columns <see cref="TeamDTO"/> needs are read.
    /// </summary>
    public static partial IQueryable<TeamDTO> ProjectToTeamDto(this IQueryable<TeamEntity> query);

    /// <summary>
    /// The member configuration for <see cref="ProjectToTeamDto"/>, which Mapperly inlines into the
    /// projection's expression tree.
    /// </summary>
    [MapProperty(nameof(@TeamEntity.Role.Name), nameof(TeamDTO.RoleName))]
    [MapProperty(nameof(TeamEntity.Permissions), nameof(TeamDTO.Permissions), Use = nameof(MapAssignedPermissions))]
    [MapProperty(nameof(TeamEntity.Scopes), nameof(TeamDTO.ScopedTeamIds), Use = nameof(MapScopedTeamIds))]
    private static partial TeamDTO MapToTeamDto(TeamEntity source);

    // Expression-bodied, so Mapperly can inline them into the projection rather than call them.
    private static List<TeamPermissionModel> MapAssignedPermissions(ICollection<TeamPermissionAssignmentEntity> assignments) =>
        assignments.Select(x => MapToTeamPermissionModel(x.Permission)).ToList();

    private static List<Guid> MapScopedTeamIds(ICollection<TeamPermissionScopeEntity> scopes) =>
        scopes.Select(x => x.TargetTeamId).ToList();

    private static partial TeamPermissionModel MapToTeamPermissionModel(TeamPermissionEntity source);

    /// <summary>
    /// The response model. <see cref="Team.IsMember"/> and <see cref="Team.IsPrimary"/> describe the
    /// caller rather than the team, so they come from <paramref name="user"/>'s claims.
    /// </summary>
    public static Team ToTeam(this TeamDTO source, ClaimsPrincipal user)
    {
        if (source == null)
            return null;

        var team = MapToTeam(source);
        team.IsMember = user.IsTeamMember(source.Id);
        team.IsPrimary = user.IsPrimaryTeam(source.Id);
        return team;
    }

    /// <inheritdoc cref="ToTeam"/>
    public static Team[] ToTeams(this IEnumerable<TeamDTO> source, ClaimsPrincipal user) =>
        [.. source.Select(x => x.ToTeam(user))];

    [MapperIgnoreTarget(nameof(Team.IsMember))]
    [MapperIgnoreTarget(nameof(Team.IsPrimary))]
    private static partial Team MapToTeam(TeamDTO source);

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>
    /// A new entity from the create command. A null <see cref="Create.Command.Id"/> or
    /// <see cref="Create.Command.RoleId"/> leaves the entity's default for the handler to fill.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial TeamEntity ToEntity(this Create.Command source);

    /// <summary>
    /// Applies the edit command to an existing entity. A null <see cref="Edit.Command.RoleId"/>
    /// keeps the team's current role.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial void ApplyTo(this Edit.Command source, TeamEntity target);
}
