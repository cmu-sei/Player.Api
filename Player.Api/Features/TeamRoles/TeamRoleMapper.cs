// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamPermissions;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.TeamRoles;

/// <summary>The TeamRoles feature's mappings.</summary>
[Mapper]
public static partial class TeamRoleMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<TeamRole> ProjectToTeamRole(this IQueryable<TeamRoleEntity> query);

    [MapProperty(nameof(TeamRoleEntity.Permissions), nameof(TeamRole.Permissions), Use = nameof(MapAssignedPermissions))]
    public static partial TeamRole ToTeamRole(this TeamRoleEntity source);

    // Expression-bodied, so Mapperly can inline it into the projection rather than call it.
    private static List<TeamPermissionModel> MapAssignedPermissions(ICollection<TeamRolePermissionEntity> assignments) =>
        assignments.Select(x => MapToTeamPermissionModel(x.Permission)).ToList();

    // TeamPermissionMapper owns this mapping; see TeamMapper for why it is declared here.
    [IncludeMappingConfiguration(nameof(@TeamPermissionMapper.ToTeamPermissionModel))]
    private static partial TeamPermissionModel MapToTeamPermissionModel(TeamPermissionEntity source);

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>
    /// A new team role from the create command. Also maps a <see cref="SeedTeamRole"/>, whose
    /// permission names the seed-data importer resolves itself.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial TeamRoleEntity ToEntity(this Create.Command source);

    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, TeamRoleEntity target);
}
