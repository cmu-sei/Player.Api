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
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Features.Webhooks;

public class GetAll
{
    [DataContract(Name = "GetWebhooksQuery")]
    public record Query : IRequest<WebhookSubscription[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("webhooks", TypedHandler)
                    .WithName("getAllWebhooks")
                    .WithDescription("Returns a list of all of the Webhook Subscriptions in the system.")
                    .WithSummary("Gets all Webhook Subscriptions in the system.")
            ];
        }

        async Task<Ok<WebhookSubscription[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, WebhookSubscription[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewWebhookSubscriptions], [], [], cancellationToken);

        public override async Task<WebhookSubscription[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            return await db.Webhooks
                .ProjectTo<WebhookSubscription>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);
        }
    }
}