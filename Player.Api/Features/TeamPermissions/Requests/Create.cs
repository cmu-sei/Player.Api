// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Features.TeamPermissions;

public class Create
{
    [DataContract(Name = "CreateTeamPermissionCommand")]
    public record Command : IRequest<TeamPermissionModel>
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("team-permissions", TypedHandler)
                    .WithName("createTeamPermission")
                    .WithDescription("Creates a new TeamPermission with the attributes specified.")
                    .WithSummary("Creates a new TeamPermission.")
            ];
        }

        async Task<CreatedAtRoute<TeamPermissionModel>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getTeamPermission", new { id = created.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, TeamPermissionModel>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<TeamPermissionModel> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var permissionEntity = mapper.Map<TeamPermissionEntity>(request);

            db.TeamPermissions.Add(permissionEntity);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<TeamPermissionModel>(permissionEntity);
        }
    }
}