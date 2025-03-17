// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
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
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class Delete
{
    [DataContract(Name = "DeleteTeamCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("teams/{id}", TypedHandler)
                    .WithName("deleteTeam")
                    .WithDescription("Deletes a Team with the specified id.")
                    .WithSummary("Deletes a Team.")
            ];
        }

        async Task<NoContent> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { Id = id }, cancellationToken);
            return TypedResults.NoContent();
        }
    }

    public class Handler(ILogger<Delete> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.Id, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var teamToDelete = await db.Teams
                .SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (teamToDelete == null)
                throw new EntityNotFoundException<Team>();

            db.Teams.Remove(teamToDelete);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning($"Team {teamToDelete.Name} ({teamToDelete.Id}) in View {teamToDelete.ViewId} deleted by {identityResolver.GetId()}");
        }
    }
}