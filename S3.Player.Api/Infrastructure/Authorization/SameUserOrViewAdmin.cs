/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

using Microsoft.AspNetCore.Authorization;
using S3.Player.Api.Data.Data.Models;
using S3.Player.Api.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace S3.Player.Api.Infrastructure.Authorization
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
