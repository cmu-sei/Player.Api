// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class ViewAdminRequirement : IAuthorizationRequirement
    {
        public Guid? ViewId { get; set; }

        public ViewAdminRequirement(Guid viewId)
        {
            ViewId = viewId;
        }

        public ViewAdminRequirement()
        {
        }
    }

    public class ViewAdminHandler : AuthorizationHandler<ViewAdminRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ViewAdminRequirement requirement)
        {
            if (context.User.HasClaim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }
            else if (requirement.ViewId.HasValue)
            {
                if (context.User.HasClaim(PlayerClaimTypes.ViewAdmin.ToString(), requirement.ViewId.ToString()))
                {
                    context.Succeed(requirement);
                }
            }
            else if (context.User.HasClaim(c => c.Type == PlayerClaimTypes.ViewAdmin.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
