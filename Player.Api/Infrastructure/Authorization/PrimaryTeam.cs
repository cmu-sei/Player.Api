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
    public class PrimaryTeamRequirement : IAuthorizationRequirement
    {
        public Guid TeamId { get; set; }

        public PrimaryTeamRequirement(Guid teamId)
        {
            TeamId = teamId;
        }
    }

    public class PrimaryTeamHandler : AuthorizationHandler<PrimaryTeamRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PrimaryTeamRequirement requirement)
        {
            if (context.User.HasClaim(PlayerClaimTypes.PrimaryTeam.ToString(), requirement.TeamId.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
