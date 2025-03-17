// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
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
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Features.Webhooks;

public class Edit
{
    [DataContract(Name = "EditWebhookSubscriptionCommand")]
    public class Command : IRequest<WebhookSubscription>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        [JsonIgnore]
        public IWebhookSubscriptionForm Form { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("webhooks/{id}", EditHandler)
                    .WithName("updateWebhookSubscription")
                    .WithDescription("Updates a Webhook Subscription with the attributes specified.")
                    .WithSummary("Updates a Webhook Subscription."),

                group.MapPatch("webhooks/{id}", PartialEditHandler)
                    .WithName("partialUpdateWebhookSubscription")
                    .WithDescription("Partially updates a Webhook Subscription with the attributes specified.")
                    .WithSummary("Partially updates a Webhook Subscription.")
            ];
        }

        async Task<Ok<WebhookSubscription>> EditHandler(Guid id, WebhookSubscriptionForm form, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(
                new Command
                {
                    Id = id,
                    Form = form
                }, cancellationToken));
        }

        async Task<Ok<WebhookSubscription>> PartialEditHandler(Guid id, WebhookSubscriptionPartialEditForm form, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(
                new Command
                {
                    Id = id,
                    Form = form
                }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, WebhookSubscription>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageWebhookSubscriptions], [], [], cancellationToken);

        public override async Task<WebhookSubscription> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var toUpdate = await db.Webhooks
                .Include(x => x.EventTypes)
                .Where(w => w.Id == request.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (toUpdate == null)
                throw new EntityNotFoundException<WebhookSubscription>();

            mapper.Map(request.Form, toUpdate);
            await db.SaveChangesAsync(cancellationToken);
            return mapper.Map<WebhookSubscription>(toUpdate);
        }
    }
}