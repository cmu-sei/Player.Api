// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Player.Api.Data.Data.Models;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Users;

/// <summary>The Users feature's mappings.</summary>
[Mapper]
public static partial class UserMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<User> ProjectToUser(this IQueryable<UserEntity> query);

    /// <summary>
    /// The directory listing. <see cref="UserDirectoryEntry.IdentityAttributes"/> is left empty for
    /// the handler to fill when the caller may see them.
    /// </summary>
    public static partial IQueryable<UserDirectoryEntry> ProjectToUserDirectoryEntry(this IQueryable<UserEntity> query);

    [MapPropertyFromSource(nameof(User.RoleName), Use = nameof(MapRoleName))]
    private static partial User MapToUser(UserEntity source);

    [IncludeMappingConfiguration(nameof(MapToUser))]
    [MapperIgnoreTarget(nameof(UserDirectoryEntry.IdentityAttributes))]
    private static partial UserDirectoryEntry MapToUserDirectoryEntry(UserEntity source);

    // The role is optional, so its name is only read when the user has one.
    private static string MapRoleName(UserEntity source) =>
        source.RoleId.HasValue ? source.Role.Name : null;

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>A new user from the create command, keeping the id the caller supplied.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial UserEntity ToEntity(this Create.Command source);

    /// <summary>A new user from seed data. The seed-data importer resolves the role by name.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(SeedUser.Role))]
    public static partial UserEntity ToEntity(this SeedUser source);

    /// <summary>
    /// Applies the edit command to an existing user. A null <see cref="Edit.Command.RoleId"/> clears
    /// the user's role.
    /// </summary>
    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, UserEntity target);
}
