using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
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

namespace Player.Api.Features.Views;

public class GetByUser
{
    [DataContract(Name = "GetViewsByUserQuery")]
    public record Query : IRequest<View[]>
    {
        public Guid UserId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users/{id}/views", GetUserViews)
                    .WithName("getUserViews")
                    .WithDescription("Returns all Views where the specified User is a member of at least one of it's Teams.")
                    .WithSummary("Gets all Views for a User."),

                group.MapGet("me/views", GetMyViews)
                    .WithName("getMyViews")
                    .WithDescription("Returns all Views where the current User is a member of at least one of it's Teams.")
                    .WithSummary("Gets all Views for the current User.")
            ];
        }

        async Task<Ok<View[]>> GetUserViews(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(
                new Query
                {
                    UserId = id,
                }, cancellationToken));
        }

        async Task<Ok<View[]>> GetMyViews(IIdentityResolver identityResolver, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(
                new Query
                {
                    UserId = identityResolver.GetId(),
                }, cancellationToken));
        }
    }

    public class Handler(IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, View[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            identityResolver.GetId() == request.UserId ||
            await authorizationService.Authorize([SystemPermission.ViewUsers], [], [], cancellationToken);

        public override async Task<View[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var user = await db.Users
                .Include(x => x.ViewMemberships)
                    .ThenInclude(x => x.View)
                .Where(x => x.Id == request.UserId)
                .SingleOrDefaultAsync(cancellationToken);

            if (user == null)
                throw new EntityNotFoundException<User>();

            var views = user.ViewMemberships.Select(x => x.View);

            return mapper.Map<View[]>(views);
        }
    }
}