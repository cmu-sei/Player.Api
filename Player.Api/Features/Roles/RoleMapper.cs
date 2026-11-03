// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Permissions;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Roles;

/// <summary>The system Roles feature's mappings.</summary>
[Mapper]
public static partial class RoleMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<Role> ProjectToRole(this IQueryable<RoleEntity> query);

    [MapProperty(nameof(RoleEntity.Permissions), nameof(Role.Permissions), Use = nameof(MapAssignedPermissions))]
    public static partial Role ToRole(this RoleEntity source);

    // Expression-bodied, so Mapperly can inline it into the projection rather than call it.
    private static List<Permission> MapAssignedPermissions(ICollection<RolePermissionEntity> assignments) =>
        assignments.Select(x => MapToPermission(x.Permission)).ToList();

    // PermissionMapper owns this mapping; see TeamMapper for why it is declared here.
    [IncludeMappingConfiguration(nameof(@PermissionMapper.ToPermission))]
    private static partial Permission MapToPermission(PermissionEntity source);

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>
    /// A new role from the create command. Also maps a <see cref="SeedRole"/>, whose permission names
    /// the seed-data importer resolves itself.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial RoleEntity ToEntity(this Create.Command source);

    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, RoleEntity target);
}
