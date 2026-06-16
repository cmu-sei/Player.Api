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

namespace Player.Api.Features.TeamPermissionScopes;

public class Add
{
    [DataContract(Name = "AddTeamPermissionScopeCommand")]
    public record Command : IRequest
    {
        public Guid TeamId { get; set; }
        public Guid TargetTeamId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("teams/{teamId}/scopes/{targetTeamId}", TypedHandler)
                    .WithName("addTeamPermissionScope")
                    .WithDescription("Scopes the specified Team's permissions onto the specified target Team in the same View.")
                    .WithSummary("Adds a Team Permission Scope.")
            ];
        }

        async Task<Ok> TypedHandler(Guid teamId, Guid targetTeamId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { TeamId = teamId, TargetTeamId = targetTeamId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(
        ILogger<Add> logger,
        IIdentityResolver identityResolver,
        IUserClaimsService claimsService,
        IPlayerAuthorizationService authorizationService,
        PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (request.TeamId == request.TargetTeamId)
                throw new ConflictException("A Team cannot scope its permissions onto itself.");

            var team = await db.Teams
                .Where(x => x.Id == request.TeamId)
                .SingleOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>("Granting Team not found.");

            var targetTeam = await db.Teams
                .Where(x => x.Id == request.TargetTeamId)
                .SingleOrDefaultAsync(cancellationToken);

            if (targetTeam == null)
                throw new EntityNotFoundException<Team>("Target Team not found.");

            if (team.ViewId != targetTeam.ViewId)
                throw new ConflictException("Both Teams must belong to the same View.");

            var exists = await db.TeamPermissionScopes
                .AnyAsync(x => x.TeamId == request.TeamId && x.TargetTeamId == request.TargetTeamId, cancellationToken);

            if (!exists)
            {
                db.TeamPermissionScopes.Add(new TeamPermissionScopeEntity(request.TeamId, request.TargetTeamId));
                await db.SaveChangesAsync(cancellationToken);

                await RefreshGrantingTeamMemberClaims(request.TeamId, cancellationToken);

                logger.LogWarning($"Team {request.TeamId} permissions scoped onto Team {request.TargetTeamId} by {identityResolver.GetId()}");
            }
        }

        private async Task RefreshGrantingTeamMemberClaims(Guid teamId, CancellationToken cancellationToken)
        {
            var userIds = await db.TeamMemberships
                .Where(x => x.TeamId == teamId)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var userId in userIds)
            {
                await claimsService.RefreshClaims(userId);
            }
        }
    }
}
