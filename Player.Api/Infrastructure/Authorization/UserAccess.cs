// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class UserAccessRequirement : IAuthorizationRequirement
    {
        public Guid UserId { get; set; }

        public UserAccessRequirement(Guid userId)
        {
            UserId = userId;
        }
    }

    public class UserAccessHandler : AuthorizationHandler<UserAccessRequirement>, IAuthorizationHandler
    {
        private readonly PlayerContext _db;

        public UserAccessHandler(PlayerContext db)
        {
            _db = db;
        }

        protected async override Task HandleRequirementAsync(AuthorizationHandlerContext context, UserAccessRequirement requirement)
        {
            if (context.User.HasClaim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }
            else if (context.User.GetId() == requirement.UserId)
            {
                context.Succeed(requirement);
            }
            else
            {
                // Check if current user shares a team with target user or is an admin of a view target user is a part of
                var teamMemberships = await _db.TeamMemberships
                    .Where(x => x.UserId == requirement.UserId)
                    .Include(x => x.ViewMembership)
                    .ToArrayAsync();

                foreach (var teamMembership in teamMemberships)
                {
                    if (context.User.HasClaim(PlayerClaimTypes.TeamMember.ToString(), teamMembership.TeamId.ToString()))
                    {
                        context.Succeed(requirement);
                        return;
                    }
                    else if (context.User.HasClaim(PlayerClaimTypes.ViewAdmin.ToString(), teamMembership.ViewMembership.ViewId.ToString()))
                    {
                        context.Succeed(requirement);
                        return;
                    }
                }
            }
        }
    }
}
