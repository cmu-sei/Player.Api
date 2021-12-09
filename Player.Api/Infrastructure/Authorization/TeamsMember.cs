// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class TeamsMemberRequirement : IAuthorizationRequirement
    {
        public List<Guid> TeamIds { get; set; }

        public TeamsMemberRequirement(List<Guid> teamIds)
        {
            TeamIds = teamIds;
        }
    }

    public class TeamsMemberHandler : AuthorizationHandler<TeamsMemberRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TeamsMemberRequirement requirement)
        {
            foreach (var teamId in requirement.TeamIds)
            {
                if (context.User.HasClaim(PlayerClaimTypes.PrimaryTeam.ToString(), teamId.ToString()))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}
