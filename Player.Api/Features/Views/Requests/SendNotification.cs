using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Player.Api.Data.Data.Models;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;

namespace Player.Api.Features.Views;

public class SendNotification
{
    [DataContract(Name = "SendViewNotificationCommand")]
    public class Command : Notification, IRequest<string>
    {
        [JsonIgnore]
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("views/{id}/notifications", TypedHandler)
                    .WithName("broadcastToView")
                    .WithDescription("Broadcasts a Notification to all members of the specified View.")
                    .WithSummary("Sends a new View Notification.")
            ];
        }

        async Task<Ok<string>> TypedHandler(Guid id, Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.ViewId = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(INotificationService notificationService, IHubContext<ViewHub> viewHub, IPlayerAuthorizationService authorizationService) : BaseHandler<Command, string>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!request.IsValid())
            {
                throw new ArgumentException($"Message was NOT sent to view {request.ViewId}");
            }

            var notification = await notificationService.PostToView(request.ViewId, request, cancellationToken);
            if (notification.ToId != request.ViewId)
            {
                throw new ForbiddenException($"Message was not sent to view {request.ViewId}");
            }

            await viewHub.Clients.Group(request.ViewId.ToString()).SendAsync("Reply", notification);
            return $"Message was sent to view {request.ViewId}";
        }
    }
}