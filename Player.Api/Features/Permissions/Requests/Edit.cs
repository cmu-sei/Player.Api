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
using Player.Api.ViewModels;

namespace Player.Api.Features.Permissions;

public class Edit
{
    [DataContract(Name = "EditPermissionCommand")]
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
                group.MapPut("permissions/{id}", TypedHandler)
                    .WithName("updatePermission")
                    .WithDescription("Updates a Permission with the attributes specified")
                    .WithSummary("Updates a Permission.")
            ];
        }

        async Task<Ok<Permission>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Permission>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<Permission> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var permissionToUpdate = await db.Permissions.SingleOrDefaultAsync(v => v.Id == request.Id);

            if (permissionToUpdate == null)
                throw new EntityNotFoundException<Permission>();

            if (permissionToUpdate.Immutable)
                throw new ForbiddenException("Cannot update an Immutable Permission");

            mapper.Map(request, permissionToUpdate);
            await db.SaveChangesAsync();

            return mapper.Map<Permission>(permissionToUpdate);
        }
    }
}