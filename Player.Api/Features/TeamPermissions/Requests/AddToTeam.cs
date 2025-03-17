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
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.TeamPermissions;

public class AddToTeam
{
    [DataContract(Name = "AddTeamPermissionToTeamCommand")]
    public record Command : IRequest
    {
        public Guid TeamId { get; set; }
        public Guid TeamPermissionId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("teams/{teamId}/permissions/{permissionId}", TypedHandler)
                    .WithName("addTeamPermissionToTeam")
                    .WithDescription("Adds the specified TeamPermission to the specified Team.")
                    .WithSummary("Adds a Team Permission to a Team.")
            ];
        }

        async Task<Ok> TypedHandler(Guid teamId, Guid permissionId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { TeamId = teamId, TeamPermissionId = permissionId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageRoles], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var teamExists = await db.Teams
                .Where(x => x.Id == request.TeamId)
                .AnyAsync(cancellationToken);

            var permissionExists = await db.TeamPermissions
                .Where(p => p.Id == request.TeamPermissionId)
                .AnyAsync(cancellationToken);

            if (!teamExists)
                throw new EntityNotFoundException<Team>();

            if (!permissionExists)
                throw new EntityNotFoundException<TeamPermissionModel>();

            db.TeamPermissionAssignments.Add(new TeamPermissionAssignmentEntity(request.TeamId, request.TeamPermissionId));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}