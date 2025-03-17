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
using Player.Api.Features.Users;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class GetByUserView
{
    [DataContract(Name = "GetTeamsByUserViewQuery")]
    public record Query : IRequest<Team[]>
    {
        public Guid ViewId { get; set; }
        public Guid UserId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users/{userId}/views/{viewId}/teams", GetViewTeams)
                    .WithName("getUserViewTeams")
                    .WithDescription("Returns all Teams within a specific View that a User is a member of.")
                    .WithSummary("Gets all Teams for a User by View."),

                group.MapGet("me/views/{id}/teams", GetMyViewTeams)
                    .WithName("getMyViewTeams")
                    .WithDescription("Returns all Teams within a specific View that the current User is a member of.")
                    .WithSummary("Gets all Teams for the current User by View.")
            ];
        }

        async Task<Ok<Team[]>> GetViewTeams(Guid viewId, Guid userId, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(
                new Query
                {
                    UserId = userId,
                    ViewId = viewId
                }, cancellationToken));
        }

        async Task<Ok<Team[]>> GetMyViewTeams(Guid id, IIdentityResolver identityResolver, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(
                new Query
                {
                    UserId = identityResolver.GetId(),
                    ViewId = id
                }, cancellationToken));
        }
    }

    public class Handler(IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, Team[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            identityResolver.GetId() == request.UserId ||
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<Team[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var viewExists = await db.Views
                .Where(e => e.Id == request.ViewId)
                .AnyAsync(cancellationToken);

            if (!viewExists)
                throw new EntityNotFoundException<View>();

            var userExists = await db.Users
                .Where(u => u.Id == request.UserId)
                .AnyAsync(cancellationToken);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            IQueryable<TeamDTO> teamQuery;

            if (await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken))
            {
                teamQuery = db.Teams
                    .Where(t => t.ViewId == request.ViewId)
                    .ProjectTo<TeamDTO>(mapper.ConfigurationProvider);
            }
            else
            {
                teamQuery = db.TeamMemberships
                .Where(x => x.UserId == request.UserId && x.Team.ViewId == request.ViewId)
                .Select(x => x.Team)
                .Distinct()
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider);
            }

            var teams = await teamQuery.ToListAsync(cancellationToken);

            return mapper.Map<Team[]>(teams);
        }
    }
}