// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public record TeamPermissionRequirement(
        ViewPermission[] RequiredViewPermissions = null,
        TeamPermission[] RequiredTeamPermissions = null,
        Guid? ViewId = null,
        Guid? TeamId = null) : IAuthorizationRequirement;

    public class TeamPermissionsHandler : AuthorizationHandler<TeamPermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            TeamPermissionRequirement requirement)
        {
            if (context.User is null)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            var claims = context.User.Claims
                .Where(x => x.Type == AuthorizationConstants.TeamPermissionsClaimType)
                .Select(x => TeamPermissionsClaim.FromString(x.Value))
                .ToList();

            if (claims.Any() && HasRequiredPermissions(claims, requirement))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        private bool HasRequiredPermissions(List<TeamPermissionsClaim> claims, TeamPermissionRequirement requirement)
        {
            // Omitted permission lists grant nothing, just like empty lists.
            var requiredViewPermissions = requirement.RequiredViewPermissions ?? [];
            var requiredTeamPermissions = requirement.RequiredTeamPermissions ?? [];

            // Check team-specific permissions
            if (requirement.TeamId.HasValue)
            {
                var teamClaim = claims.FirstOrDefault(x => x.TeamId == requirement.TeamId.Value);
                if (teamClaim?.TeamPermissions.Intersect(requiredTeamPermissions).Any() == true)
                {
                    return true;
                }
            }

            // Check view-specific permissions
            if (requirement.ViewId.HasValue)
            {
                var viewClaims = claims.Where(x => x.ViewId == requirement.ViewId.Value);
                if (requiredViewPermissions.Any(x => viewClaims.SelectMany(y => y.ViewPermissions).Contains(x)))
                {
                    return true;
                }
            }

            // Check all permissions when no specific team or view is specified
            if (!requirement.TeamId.HasValue && !requirement.ViewId.HasValue)
            {
                return
                    requiredViewPermissions.Any(x => claims.SelectMany(y => y.ViewPermissions).Contains(x)) ||
                    requiredTeamPermissions.Any(x => claims.SelectMany(y => y.TeamPermissions).Contains(x));
            }

            return false;
        }
    }
}
