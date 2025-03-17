// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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

namespace Player.Api.Features.TeamRoles;

public class GetAll
{
    [DataContract(Name = "GetTeamRolesQuery")]
    public record Query : IRequest<TeamRole[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("team-roles", TypedHandler)
                    .WithName("getTeamRoles")
                    .WithDescription("Returns a list of all of the Team Roles in the system.")
                    .WithSummary("Gets all Team Roles in the system.")
            ];
        }

        async Task<Ok<TeamRole[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, TeamRole[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewRoles, SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<TeamRole[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            return await db.TeamRoles
                .ProjectTo<TeamRole>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);
        }
    }
}