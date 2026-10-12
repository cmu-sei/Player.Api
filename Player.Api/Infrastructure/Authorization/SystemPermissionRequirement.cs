// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class SystemPermissionRequirement : IAuthorizationRequirement
    {
        public SystemPermission[] RequiredPermissions;

        public SystemPermissionRequirement(SystemPermission[] requiredPermissions)
        {
            RequiredPermissions = requiredPermissions;
        }
    }

    public class SystemPermissionsHandler : AuthorizationHandler<SystemPermissionRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SystemPermissionRequirement requirement)
        {
            // An empty list grants nothing. AuthorizationService treats a passing system requirement as an
            // administrative bypass of the view and team checks, so succeeding here would let every caller through.
            if (context.User == null)
            {
                context.Fail();
            }
            else if (requirement.RequiredPermissions != null && requirement.RequiredPermissions.Any(p => context.User.HasClaim(AuthorizationConstants.PermissionsClaimType, p.ToString())))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
