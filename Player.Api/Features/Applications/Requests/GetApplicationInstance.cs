// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Player.Api.ViewModels;
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.Applications;

public class GetApplicationInstance
{
    [DataContract(Name = "GetApplicationInstanceQuery")]
    public class Query : IRequest<ApplicationInstance>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("application-instances/{id}", TypedHandler)
                    .WithName("getApplicationInstance")
                    .WithDescription("Returns the Application Instance with the id specified.")
                    .WithSummary("Gets a specific Application Instance by id.")
            ];
        }

        async Task<Ok<ApplicationInstance>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, ApplicationInstance>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ApplicationInstanceEntity>(request.Id, [SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ViewTeam], cancellationToken);

        public override async Task<ApplicationInstance> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var instance = await db.ApplicationInstances
                .ProjectTo<ApplicationInstance>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            return instance;
        }
    }
}