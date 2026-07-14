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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Features.TeamPermissionScopes;

public class Remove
{
    [DataContract(Name = "RemoveTeamPermissionScopeCommand")]
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
                group.MapDelete("teams/{teamId}/scopes/{targetTeamId}", TypedHandler)
                    .WithName("removeTeamPermissionScope")
                    .WithDescription("Removes the permission scope of the specified Team from the specified target Team.")
                    .WithSummary("Removes a Team Permission Scope.")
            ];
        }

        async Task<Ok> TypedHandler(Guid teamId, Guid targetTeamId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { TeamId = teamId, TargetTeamId = targetTeamId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(
        ILogger<Remove> logger,
        IIdentityResolver identityResolver,
        IPlayerAuthorizationService authorizationService,
        PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var scope = await db.TeamPermissionScopes
                .Where(x => x.TeamId == request.TeamId && x.TargetTeamId == request.TargetTeamId)
                .SingleOrDefaultAsync(cancellationToken);

            if (scope != null)
            {
                db.TeamPermissionScopes.Remove(scope);
                await db.SaveChangesAsync(cancellationToken);

                logger.LogWarning($"Team {request.TeamId} permission scope removed from Team {request.TargetTeamId} by {identityResolver.GetId()}");
            }
        }
    }
}
