// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;

namespace Player.Api.Features.Users;

public class RemoveFromTeam
{
    [DataContract(Name = "RemoveUserFromTeamCommand")]
    public record Command : IRequest
    {
        public Guid TeamId { get; set; }
        public Guid UserId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("teams/{teamId}/users/{userId}", TypedHandler)
                    .WithName("removeUserFromTeam")
                    .WithDescription("Removes the specified User from the specified Team.")
                    .WithSummary("Removes a User from a Team.")
            ];
        }

        async Task<Ok> TypedHandler(Guid teamId, Guid userId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { TeamId = teamId, UserId = userId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(
        ILogger<RemoveFromTeam> logger,
        IIdentityResolver identityResolver,
        IUserClaimsService claimsService,
        IPlayerAuthorizationService authorizationService,
        PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var team = await db.Teams
               .Where(t => t.Id == request.TeamId)
               .FirstOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var userExists = await db.Users
                .Where(u => u.Id == request.UserId)
                .AnyAsync(cancellationToken);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var teamMemberships = await db.TeamMemberships
                .Include(m => m.Team)
                .Where(m => m.UserId == request.UserId)
                .ToArrayAsync(cancellationToken);

            var teamMembership = teamMemberships.SingleOrDefault(tu => tu.TeamId == request.TeamId);

            if (teamMembership != null)
            {
                var viewMembership = db.ViewMemberships
                    .SingleOrDefault(x => x.UserId == request.UserId && x.ViewId == team.ViewId);

                if (teamMemberships.Where(m => m.Team.ViewId == team.ViewId).Count() == 1)
                {
                    db.TeamMemberships.Remove(teamMembership);
                    viewMembership.PrimaryTeamMembershipId = null;
                    await db.SaveChangesAsync(cancellationToken);

                    db.ViewMemberships.Remove(viewMembership);
                }
                else if (viewMembership.PrimaryTeamMembershipId == teamMembership.Id)
                {
                    // Set a new primary Team if we are deleting the current one
                    Guid newPrimaryTeamMembershipId = teamMemberships.Where(m => m.Team.ViewId == team.ViewId && m.TeamId != request.TeamId).FirstOrDefault().Id;
                    viewMembership.PrimaryTeamMembershipId = newPrimaryTeamMembershipId;
                    db.ViewMemberships.Update(viewMembership);
                    await db.SaveChangesAsync(cancellationToken);

                    db.TeamMemberships.Remove(teamMembership);
                }
                else
                {
                    db.TeamMemberships.Remove(teamMembership);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            logger.LogWarning($"User {request.UserId} removed from team {request.TeamId} by {identityResolver.GetId()}");
            await claimsService.RefreshClaims(request.UserId);
        }
    }
}