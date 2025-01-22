using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
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

namespace Player.Api.Features.Roles;

public class Edit
{
    [DataContract(Name = "EditRoleCommand")]
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
                group.MapPut("roles/{id}", TypedHandler)
                    .WithName("updateRole")
                    .WithDescription("Updates a Role with the attributes specified.")
                    .WithSummary("Updates a Role.")
            ];
        }

        async Task<Ok<Role>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Role>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<Role> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var roleToUpdate = await db.Roles.SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (roleToUpdate == null)
                throw new EntityNotFoundException<Role>();

            mapper.Map(request, roleToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            return await db.Roles
                .ProjectTo<Role>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        }
    }
}