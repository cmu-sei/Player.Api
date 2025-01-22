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
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class GetAll
{
    [DataContract(Name = "GetTeamsQuery")]
    public record Query : IRequest<Team[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("teams", TypedHandler)
                    .WithName("getTeams")
                    .WithDescription("Returns a list of all of the Teams in the system.")
                    .WithSummary("Gets all Teams in the system.")
            ];
        }

        async Task<Ok<Team[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, Team[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewViews], [], [], cancellationToken);

        public override async Task<Team[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var items = await db.Teams
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);
            return mapper.Map<Team[]>(items);
        }
    }
}