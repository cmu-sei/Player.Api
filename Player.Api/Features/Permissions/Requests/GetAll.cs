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
using Player.Api.ViewModels;
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.Permissions;

public class GetAll
{
    [DataContract(Name = "GetPermissionsQuery")]
    public class Query : IRequest<Permission[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("permissions", TypedHandler)
                    .WithName("getPermissions")
                    .WithDescription("Returns a list of all of the Permissions in the system.")
                    .WithSummary("Gets all Permissions in the system.")
            ];
        }

        async Task<Ok<Permission[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, Permission[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewRoles], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<Permission[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            return await db.Permissions
                .ProjectTo<Permission>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);
        }
    }
}