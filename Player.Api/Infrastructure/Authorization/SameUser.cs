// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
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
    public class SameUserRequirement : IAuthorizationRequirement
    {
        public Guid UserId { get; set; }

        public SameUserRequirement(Guid userId)
        {
            UserId = userId;
        }
    }

    public class SameUserHandler : AuthorizationHandler<SameUserRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameUserRequirement requirement)
        {
            if (context.User.HasClaim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }
            else if (context.User.GetId() == requirement.UserId)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
