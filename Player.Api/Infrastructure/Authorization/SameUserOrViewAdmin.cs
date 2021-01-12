// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class SameUserOrViewAdminRequirement : IAuthorizationRequirement
    {
        public Guid ViewId { get; set; }
        public Guid UserId { get; set; }

        public SameUserOrViewAdminRequirement(Guid viewId, Guid userId)
        {
            ViewId = viewId;
            UserId = userId;
        }
    }

    public class SameUserOrViewAdminHandler : AuthorizationHandler<SameUserOrViewAdminRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameUserOrViewAdminRequirement requirement)
        {
            if (context.User.GetId() == requirement.UserId)
            {
                context.Succeed(requirement);
            }
            else
            {
                if (context.User.HasClaim(PlayerClaimTypes.ViewMember.ToString(), requirement.ViewId.ToString()))
                {
                    if (context.User.HasClaim(PlayerClaimTypes.ViewAdmin.ToString(), requirement.ViewId.ToString()))
                    {
                        context.Succeed(requirement);
                    }
                }
                else
                {
                    if (context.User.HasClaim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()))
                    {
                        context.Succeed(requirement);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
