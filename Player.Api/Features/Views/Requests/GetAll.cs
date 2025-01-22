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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Features.Views;

public class GetAll
{
    [DataContract(Name = "GetViewsQuery")]
    public record Query : IRequest<View[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views", TypedHandler)
                    .WithName("getViews")
                    .WithDescription("Returns a list of all of the Views in the system.")
                    .WithSummary("Gets all Views in the system.")
            ];
        }

        async Task<Ok<View[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, View[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewViews], [], [], cancellationToken);

        public override async Task<View[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var items = await db.Views
                .ToArrayAsync(cancellationToken);

            return mapper.Map<View[]>(items);
        }
    }
}