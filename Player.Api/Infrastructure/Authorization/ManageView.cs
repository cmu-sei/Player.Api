// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class ManageViewRequirement : IAuthorizationRequirement
    {
        public Guid ViewId { get; set; }

        public ManageViewRequirement(Guid viewId)
        {
            ViewId = viewId;
        }
    }

    public class ManageViewHandler : AuthorizationHandler<ManageViewRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ManageViewRequirement requirement)
        {
            if (context.User.HasClaim(PlayerClaimTypes.ViewMember.ToString(), requirement.ViewId.ToString()))
            {
                if (context.User.HasClaim(PlayerClaimTypes.ViewAdmin.ToString(), requirement.ViewId.ToString()))
                {
                    context.Succeed(requirement);
                }
            }
            else if (context.User.HasClaim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
