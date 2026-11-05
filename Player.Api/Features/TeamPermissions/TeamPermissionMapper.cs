// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Player.Api.Data.Data.Models;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.TeamPermissions;

/// <summary>The TeamPermissions feature's mappings.</summary>
[Mapper]
public static partial class TeamPermissionMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<TeamPermissionModel> ProjectToTeamPermissionModel(this IQueryable<TeamPermissionEntity> query);

    public static partial TeamPermissionModel ToTeamPermissionModel(this TeamPermissionEntity source);

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>A new, mutable team permission from the create command.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial TeamPermissionEntity ToEntity(this Create.Command source);

    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, TeamPermissionEntity target);
}
