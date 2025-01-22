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

namespace Player.Api.Features.TeamRoles;

public class Get
{
    [DataContract(Name = "GetTeamRoleQuery")]
    public class Query : IRequest<TeamRole>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("team-roles/{id}", TypedHandler)
                    .WithName("getTeamRole")
                    .WithDescription("Returns the Team Role with the id specified.")
                    .WithSummary("Gets a specific Team Role by id.")
            ];
        }

        async Task<Ok<TeamRole>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, TeamRole>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewRoles, SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<TeamRole> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.TeamRoles
                .ProjectTo<TeamRole>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (item == null)
                throw new EntityNotFoundException<TeamRole>();

            return item;
        }
    }
}