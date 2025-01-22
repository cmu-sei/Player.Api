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
using Player.Api.ViewModels;

namespace Player.Api.Features.TeamMemberships;

public class GetByUserView
{
    [DataContract(Name = "GetTeamMembershipsByUserViewQuery")]
    public record Query : IRequest<TeamMembership[]>
    {
        public Guid UserId { get; set; }
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users/{userId}/views/{viewId}/team-memberships", TypedHandler)
                    .WithName("getTeamMemberships")
                    .WithDescription("Returns a list of all of the Team Memberships for a User in the specified View.")
                    .WithSummary("Gets all Team Memberships for a User by View."),
            ];
        }

        async Task<Ok<TeamMembership[]>> TypedHandler(Guid userId, Guid viewId, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { UserId = userId, ViewId = viewId }, cancellationToken));
        }
    }

    public class Handler(IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, TeamMembership[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            identityResolver.GetId() == request.UserId ||
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<TeamMembership[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var userExists = await db.Users
                .Where(u => u.Id == request.UserId)
                .AnyAsync(cancellationToken);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var memberships = await db.TeamMemberships
                .Where(m => m.UserId == request.UserId && m.ViewMembership.ViewId == request.ViewId)
                .ProjectTo<TeamMembership>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return memberships;
        }
    }
}