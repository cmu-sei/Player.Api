using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Features.Webhooks;

public class Create
{
    [DataContract(Name = "CreateWebhookSubscriptionCommand")]
    public class Command : WebhookSubscriptionForm, IRequest<WebhookSubscription>
    {
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("webhooks/subscribe", TypedHandler)
                    .WithName("createWebhookSubscription")
                    .WithDescription("Creates a subscription to send specified events to the target.")
                    .WithSummary("Subscribes to an event.")
            ];
        }

        async Task<Ok<WebhookSubscription>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, WebhookSubscription>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageWebhookSubscriptions], [], [], cancellationToken);

        public override async Task<WebhookSubscription> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var entity = mapper.Map<WebhookSubscriptionEntity>(request);
            db.Webhooks.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<WebhookSubscription>(entity);
        }
    }
}