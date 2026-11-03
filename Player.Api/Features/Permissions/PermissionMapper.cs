// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Player.Api.Data.Data.Models;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Permissions;

/// <summary>The system Permissions feature's mappings.</summary>
[Mapper]
public static partial class PermissionMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<Permission> ProjectToPermission(this IQueryable<PermissionEntity> query);

    public static partial Permission ToPermission(this PermissionEntity source);

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>A new, mutable permission from the create command.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial PermissionEntity ToEntity(this Create.Command source);

    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, PermissionEntity target);
}
