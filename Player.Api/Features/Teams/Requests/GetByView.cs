// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class GetByView
{
    [DataContract(Name = "GetTeamsByViewQuery")]
    public class Query : IRequest<Team[]>
    {
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views/{id}/teams", TypedHandler)
                    .WithName("getViewTeams")
                    .WithDescription("Returns all Teams within a specific View.")
                    .WithSummary("Gets all Teams for a View.")
            ];
        }

        async Task<Ok<Team[]>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { ViewId = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, Team[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<Team[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var viewExists = await db.Views
                .Where(e => e.Id == request.ViewId)
                .AnyAsync(cancellationToken);

            if (!viewExists)
                throw new EntityNotFoundException<View>();

            var teams = await db.Teams
                .Where(e => e.ViewId == request.ViewId)
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return mapper.Map<Team[]>(teams);
        }
    }
}