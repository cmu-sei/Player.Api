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
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class Get
{
    [DataContract(Name = "GetTeamQuery")]
    public class Query : IRequest<Team>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("teams/{id}", TypedHandler)
                    .WithName("getTeam")
                    .WithDescription("Returns the Team with the id specified.")
                    .WithSummary("Gets a specific Team by id.")
            ];
        }

        async Task<Ok<Team>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, Team>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.Id, [SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ViewTeam], cancellationToken);

        public override async Task<Team> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var team = await db.Teams
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            return mapper.Map<Team>(team);
        }
    }
}