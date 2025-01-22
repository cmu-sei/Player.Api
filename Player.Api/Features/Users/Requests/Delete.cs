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

namespace Player.Api.Features.Users;

public class Delete
{
    [DataContract(Name = "DeleteUserCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("users/{id}", TypedHandler)
                    .WithName("deleteUser")
                    .WithDescription("Deletes a User with the specified id.")
                    .WithSummary("Deletes a User.")
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
            await authorizationService.Authorize([SystemPermission.ManageUsers], [], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (request.Id == identityResolver.GetId())
            {
                throw new ForbiddenException("You cannot delete your own account");
            }

            var userToDelete = await db.Users
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (userToDelete == null)
                throw new EntityNotFoundException<User>();

            db.Users.Remove(userToDelete);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning($"User {userToDelete.Name} ({userToDelete.Id}) deleted by {identityResolver.GetId()}");
        }
    }
}