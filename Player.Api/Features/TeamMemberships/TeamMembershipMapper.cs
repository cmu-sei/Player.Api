// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Player.Api.Data.Data.Models;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.TeamMemberships;

/// <summary>The TeamMemberships feature's mappings.</summary>
[Mapper]
public static partial class TeamMembershipMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<TeamMembership> ProjectToTeamMembership(this IQueryable<TeamMembershipEntity> query);

    [MapProperty(nameof(@TeamMembershipEntity.User.Name), nameof(TeamMembership.UserName))]
    [MapProperty(nameof(@TeamMembershipEntity.Team.Name), nameof(TeamMembership.TeamName))]
    [MapProperty(nameof(@TeamMembershipEntity.ViewMembership.ViewId), nameof(TeamMembership.ViewId))]
    [MapPropertyFromSource(nameof(TeamMembership.RoleName), Use = nameof(MapRoleName))]
    [MapPropertyFromSource(nameof(TeamMembership.isPrimary), Use = nameof(IsPrimary))]
    private static partial TeamMembership MapToTeamMembership(TeamMembershipEntity source);

    // Expression-bodied, so Mapperly can inline them into the projection rather than call them.
    // The role is optional, so its name is only read when the membership has one.
    private static string MapRoleName(TeamMembershipEntity source) =>
        source.RoleId.HasValue ? source.Role.Name : null;

    private static bool IsPrimary(TeamMembershipEntity source) =>
        source.ViewMembership.PrimaryTeamMembershipId == source.Id;

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>
    /// Applies the edit command to an existing membership. A null <see cref="Edit.Command.RoleId"/>
    /// clears the membership's role.
    /// </summary>
    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, TeamMembershipEntity target);
}
