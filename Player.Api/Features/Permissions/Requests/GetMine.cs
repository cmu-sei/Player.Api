// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Features.Permissions;

public class GetMine
{
    [DataContract(Name = "GetMyPermissionsQuery")]
    public class Query : IRequest<string[]>
    {
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("permissions/mine", TypedHandler)
                    .WithName("getMyPermissions")
                    .WithDescription("Returns all System Permissions for the current user.")
                    .WithSummary("Gets System Permissions for the current User.")
            ];
        }

        async Task<Ok<string[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService) : BaseHandler<Query, string[]>
    {
        // Anyone can access since it only returns results for the current User
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

        public override Task<string[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            return Task.FromResult(authorizationService.GetSystemPermissions().ToArray());
        }
    }
}