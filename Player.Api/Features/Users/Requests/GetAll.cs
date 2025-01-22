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
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.Users;

public class GetAll
{
    [DataContract(Name = "GetUsersQuery")]
    public record Query : IRequest<User[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users", TypedHandler)
                    .WithName("getUsers")
                    .WithDescription("Returns a list of all of the Users in the system.")
                    .WithSummary("Gets all Users in the system.")
            ];
        }

        async Task<Ok<User[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, User[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewUsers], [ViewPermission.ManageView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<User[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            return await db.Users
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);
        }
    }
}