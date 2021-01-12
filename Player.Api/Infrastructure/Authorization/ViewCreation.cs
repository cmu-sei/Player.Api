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
    public class ViewCreationRequirement : IAuthorizationRequirement
    {
    }

    public class ViewCreationHandler : AuthorizationHandler<ViewCreationRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ViewCreationRequirement requirement)
        {
            if (context.User.HasClaim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }
            //else if(context.User.HasClaim(ClaimTypes.Role, UserRole.Administrator.ToString()))
            //{
            //    context.Succeed(requirement);
            //}

            return Task.CompletedTask;
        }
    }
}
