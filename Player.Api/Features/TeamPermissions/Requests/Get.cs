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

namespace Player.Api.Features.TeamPermissions;

public class Get
{
    [DataContract(Name = "GetTeamPermissionQuery")]
    public class Query : IRequest<TeamPermissionModel>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("team-permissions/{id}", TypedHandler)
                    .WithName("getTeamPermission")
                    .WithDescription("Returns the Team Permission with the id specified.")
                    .WithSummary("Gets a specific Team Permission by id.")
            ];
        }

        async Task<Ok<TeamPermissionModel>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, TeamPermissionModel>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewRoles, SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<TeamPermissionModel> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var permission = await db.TeamPermissions
                .ProjectTo<TeamPermissionModel>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(x => x.Id == request.Id);

            if (permission == null)
                throw new EntityNotFoundException<TeamPermissionModel>();

            return permission;
        }
    }
}