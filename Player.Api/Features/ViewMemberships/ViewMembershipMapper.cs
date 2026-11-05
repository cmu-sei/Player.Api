// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Player.Api.Data.Data.Models;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.ViewMemberships;

/// <summary>The ViewMemberships feature's mappings.</summary>
[Mapper]
public static partial class ViewMembershipMapper
{
    public static partial IQueryable<ViewMembership> ProjectToViewMembership(this IQueryable<ViewMembershipEntity> query);

    [MapProperty(nameof(@ViewMembershipEntity.User.Name), nameof(ViewMembership.UserName))]
    [MapProperty(nameof(@ViewMembershipEntity.View.Name), nameof(ViewMembership.ViewName))]
    [MapPropertyFromSource(nameof(ViewMembership.PrimaryTeamId), Use = nameof(MapPrimaryTeamId))]
    [MapProperty(nameof(@ViewMembershipEntity.PrimaryTeamMembership.Team.Name), nameof(ViewMembership.PrimaryTeamName))]
    private static partial ViewMembership MapToViewMembership(ViewMembershipEntity source);

    // Deliberately unguarded, as the AutoMapper map was. Mapperly's own path mapping would read a
    // missing primary team as Guid.Empty; this keeps today's behavior, where EF Core fails to
    // materialize the null and the request answers 500. ViewMembershipRequestTests pins that.
    private static Guid MapPrimaryTeamId(ViewMembershipEntity source) =>
        source.PrimaryTeamMembership.TeamId;
}
