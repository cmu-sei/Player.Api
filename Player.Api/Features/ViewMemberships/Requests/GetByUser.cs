using System;
using System.Linq;
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
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Users;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.ViewMemberships;

public class GetByUser
{
    [DataContract(Name = "GetViewMembershipsByUserQuery")]
    public class Query : IRequest<ViewMembership[]>
    {
        public Guid UserId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users/{userId}/view-memberships", TypedHandler)
                    .WithName("getViewMemberships")
                    .WithDescription("Returns all View Memberships for the specified User.")
                    .WithSummary("Gets all View Memberships for a User.")
            ];
        }

        async Task<Ok<ViewMembership[]>> TypedHandler(Guid userId, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { UserId = userId }, cancellationToken));
        }
    }

    public class Handler(IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, ViewMembership[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            identityResolver.GetId() == request.UserId ||
            await authorizationService.Authorize([SystemPermission.ViewUsers], [], [], cancellationToken);

        public override async Task<ViewMembership[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var userExists = await db.Users
               .Where(u => u.Id == request.UserId)
               .AnyAsync(cancellationToken);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var memberships = await db.ViewMemberships
                .Where(m => m.UserId == request.UserId)
                .ProjectTo<ViewMembership>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return memberships;
        }
    }
}