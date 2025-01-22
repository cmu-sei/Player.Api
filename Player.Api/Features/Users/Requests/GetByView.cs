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
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Users;

public class GetByView
{
    [DataContract(Name = "GetUsersByViewQuery")]
    public class Query : IRequest<User[]>
    {
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views/{id}/users", TypedHandler)
                    .WithName("getViewUsers")
                    .WithDescription("Returns all Users within a specific View.")
                    .WithSummary(" Gets all Users for a View.")
            ];
        }

        async Task<Ok<User[]>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { ViewId = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, User[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewUsers], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<User[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var viewExists = await db.Views
                .Where(e => e.Id == request.ViewId)
                .AnyAsync(cancellationToken);

            if (!viewExists)
                throw new EntityNotFoundException<View>();

            var users = await db.ViewMemberships
                .Where(m => m.ViewId == request.ViewId)
                .Select(m => m.User)
                .Distinct()
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return users;
        }
    }
}