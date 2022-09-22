// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Authorization
{
    public class TeamMemberRequirement : IAuthorizationRequirement
    {
        public Guid TeamId { get; set; }

        public TeamMemberRequirement(Guid teamId)
        {
            TeamId = teamId;
        }
    }

    public class TeamMemberHandler : AuthorizationHandler<TeamMemberRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TeamMemberRequirement requirement)
        {
            if (context.User.HasClaim(PlayerClaimTypes.TeamMember.ToString(), requirement.TeamId.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
