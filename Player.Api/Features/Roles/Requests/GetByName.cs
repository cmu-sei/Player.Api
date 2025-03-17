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
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Roles;

public class GetByName
{
    [DataContract(Name = "GetRoleByNameQuery")]
    public class Query : IRequest<Role>
    {
        public string Name { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("roles/name/{name}", TypedHandler)
                    .WithName("getRoleByName")
                    .WithDescription("Returns the Role with the name specified.")
                    .WithSummary("Gets a specific Role by name.")
            ];
        }

        async Task<Ok<Role>> TypedHandler(string name, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Name = name }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, Role>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewRoles, SystemPermission.ViewUsers], [], [], cancellationToken);

        public override async Task<Role> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.Roles
                .ProjectTo<Role>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Name == request.Name);

            if (item == null)
                throw new EntityNotFoundException<Role>();

            return item;
        }
    }
}