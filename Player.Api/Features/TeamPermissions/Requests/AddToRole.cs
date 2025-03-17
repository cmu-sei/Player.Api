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
using Player.Api.Features.TeamRoles;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.TeamPermissions;

public class AddToRole
{
    [DataContract(Name = "AddTeamPermissionToRoleCommand")]
    public record Command : IRequest
    {
        public Guid RoleId { get; set; }
        public Guid TeamPermissionId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("team-roles/{roleId}/permissions/{permissionId}", TypedHandler)
                    .WithName("addTeamPermissionToRole")
                    .WithDescription("Adds the specified TeamPermission to the specified Team Role.")
                    .WithSummary("Adds a Team Permission to a Team Role.")
            ];
        }

        async Task<Ok> TypedHandler(Guid roleId, Guid permissionId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { RoleId = roleId, TeamPermissionId = permissionId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var role = await db.TeamRoles
                .Where(r => r.Id == request.RoleId)
                .SingleOrDefaultAsync(cancellationToken);

            var permission = await db.TeamPermissions
                .Where(p => p.Id == request.TeamPermissionId)
                .SingleOrDefaultAsync(cancellationToken);

            if (role == null)
                throw new EntityNotFoundException<TeamRole>();

            if (permission == null)
                throw new EntityNotFoundException<TeamPermissionModel>();

            role.Permissions.Add(new TeamRolePermissionEntity(request.RoleId, request.TeamPermissionId));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}