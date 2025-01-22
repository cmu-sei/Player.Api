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
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Users;

public class GetByTeam
{
    [DataContract(Name = "GetUsersByTeamQuery")]
    public record Query : IRequest<User[]>
    {
        public Guid TeamId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("teams/{id}/users", TypedHandler)
                    .WithName("getTeamUsers")
                    .WithDescription("Returns all Users within a specific Team.")
                    .WithSummary("Gets all Users for a Team."),
            ];
        }

        async Task<Ok<User[]>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { TeamId = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, User[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ViewTeam], cancellationToken);

        public override async Task<User[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var team = await db.Teams
                .Where(t => t.Id == request.TeamId)
                .FirstOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var users = await db.TeamMemberships
                .Where(t => t.TeamId == team.Id)
                .Select(m => m.User)
                .Distinct()
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return users;
        }
    }
}