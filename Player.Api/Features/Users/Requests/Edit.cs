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
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Users;

public class Edit
{
    [DataContract(Name = "EditUserCommand")]
    public record Command : IRequest<User>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? RoleId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("users/{id}", TypedHandler)
                    .WithName("updateUser")
                    .WithDescription("Updates a User with the attributes specified.")
                    .WithSummary("Updates a User.")
            ];
        }

        async Task<Ok<User>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(ILogger<Edit> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, User>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageUsers], [], [], cancellationToken);

        public override async Task<User> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var userToUpdate = await db.Users
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (userToUpdate == null)
                throw new EntityNotFoundException<User>();

            mapper.Map(request, userToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning($"User {userToUpdate.Name} ({userToUpdate.Id}) updated by {identityResolver.GetId()}");

            return await db.Users
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        }
    }
}