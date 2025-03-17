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
using Player.Api.Infrastructure.Exceptions;
using ViewPermission = Player.Api.Data.Data.Models.ViewPermission;

namespace Player.Api.Features.ViewMemberships;

public class Get
{
    [DataContract(Name = "GetViewMembershipsQuery")]
    public class Query : IRequest<ViewMembership>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("view-memberships/{id}", TypedHandler)
                    .WithName("getViewMembership")
                    .WithDescription("Returns the View Membership with the id specified.")
                    .WithSummary("Gets a specific View Membership by id.")
            ];
        }

        async Task<Ok<ViewMembership>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, ViewMembership>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            // TODO: Allow same user?
            await authorizationService.Authorize<ViewMembershipEntity>(request.Id, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<ViewMembership> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.ViewMemberships
                 .ProjectTo<ViewMembership>(mapper.ConfigurationProvider)
                 .SingleOrDefaultAsync(o => o.Id == request.Id);

            if (item == null)
                throw new EntityNotFoundException<ViewMembership>();

            return item;
        }
    }
}