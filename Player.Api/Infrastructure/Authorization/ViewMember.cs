// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
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
    public class ViewMemberRequirement : IAuthorizationRequirement
    {
        public Guid ViewId { get; set; }

        public ViewMemberRequirement(Guid viewId)
        {
            ViewId = viewId;
        }
    }

    public class ViewMemberHandler : AuthorizationHandler<ViewMemberRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ViewMemberRequirement requirement)
        {
            if (context.User.HasClaim(PlayerClaimTypes.ViewMember.ToString(), requirement.ViewId.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
