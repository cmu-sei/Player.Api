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
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.TeamMemberships;

public class Get
{
    [DataContract(Name = "GetTeamMembershipQuery")]
    public class Query : IRequest<TeamMembership>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("team-memberships/{id}", TypedHandler)
                    .WithName("getTeamMembership")
                    .WithDescription("Returns the Team Membership with the id specified.")
                    .WithSummary("Gets a specific Team Membership by id.")
            ];
        }

        async Task<Ok<TeamMembership>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, TeamMembership>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            // TODO: Allow same user?
            await authorizationService.Authorize<TeamMembershipEntity>(request.Id, [SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<TeamMembership> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.TeamMemberships
                .ProjectTo<TeamMembership>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (item == null)
                throw new EntityNotFoundException<TeamMembership>();

            return item;
        }
    }
}