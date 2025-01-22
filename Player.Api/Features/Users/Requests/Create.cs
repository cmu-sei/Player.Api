using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
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

namespace Player.Api.Features.Users;

public class Create
{
    [DataContract(Name = "CreateUserCommand")]
    public record Command : IRequest<User>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? RoleId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("users", TypedHandler)
                    .WithName("createUser")
                    .WithDescription("Creates a new User with the attributes specified.")
                    .WithSummary("Creates a new User.")
            ];
        }

        async Task<CreatedAtRoute<User>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getUser", new { id = created.Id });
        }
    }

    public class Handler(ILogger<Create> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, User>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageUsers], [], [], cancellationToken);

        public override async Task<User> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var userEntity = mapper.Map<UserEntity>(request);

            db.Users.Add(userEntity);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning($"User {request.Name} ({userEntity.Id}) created by {identityResolver.GetId()}");

            return await db.Users
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == userEntity.Id, cancellationToken);
        }
    }
}