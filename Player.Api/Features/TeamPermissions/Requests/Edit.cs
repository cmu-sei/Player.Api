using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.TeamPermissions;

public class Edit
{
    [DataContract(Name = "EditTeamPermissionCommand")]
    public record Command : Create.Command
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("team-permissions/{id}", TypedHandler)
                    .WithName("updateTeamPermission")
                    .WithDescription("Updates a TeamPermission with the attributes specified")
                    .WithSummary("Updates a TeamPermission.")
            ];
        }

        async Task<Ok<TeamPermissionModel>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, TeamPermissionModel>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<TeamPermissionModel> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var permissionToUpdate = await db.TeamPermissions.SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (permissionToUpdate == null)
                throw new EntityNotFoundException<TeamPermissionModel>();

            if (permissionToUpdate.Immutable)
                throw new ForbiddenException("Cannot update an Immutable TeamPermissionModel");

            mapper.Map(request, permissionToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<TeamPermissionModel>(permissionToUpdate);
        }
    }
}