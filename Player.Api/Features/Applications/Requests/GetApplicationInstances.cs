// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
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

namespace Player.Api.Features.Applications;

public class GetApplicationInstancesByTeam
{
    [DataContract(Name = "GetApplicationInstancesByTeamQuery")]
    public class Query : IRequest<IEnumerable<ApplicationInstance>>
    {
        public Guid TeamId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("teams/{id}/application-instances", TypedHandler)
                    .WithName("getTeamApplicationInstances")
                    .WithDescription("Returns all Application Instances assigned to a specific Team.")
                    .WithSummary("Gets all Applications Instances for a Team.")
            ];
        }

        async Task<Ok<IEnumerable<ApplicationInstance>>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { TeamId = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, IEnumerable<ApplicationInstance>>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ViewTeam], cancellationToken);

        public override async Task<IEnumerable<ApplicationInstance>> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var team = await db.Teams
                .Where(e => e.Id == request.TeamId)
                .SingleOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var instances = await db.ApplicationInstances
                .Where(i => i.TeamId == request.TeamId)
                .OrderBy(a => a.DisplayOrder)
                .ProjectTo<ApplicationInstance>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return instances;
        }
    }
}