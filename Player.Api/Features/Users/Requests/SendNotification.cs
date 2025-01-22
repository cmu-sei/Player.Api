using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Player.Api.Data.Data.Models;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;

namespace Player.Api.Features.Users;

public class SendNotification
{
    [DataContract(Name = "SendUserNotificationCommand")]
    public class Command : Notification, IRequest<string>
    {
        [JsonIgnore]
        public Guid UserId { get; set; }

        [JsonIgnore]
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("views/{viewId}/users/{userId}/notifications", TypedHandler)
                    .WithName("broadcastToUser")
                    .WithDescription("Send a Notification the User in the specified View.")
                    .WithSummary("Sends a new User Notification.")
            ];
        }

        async Task<Ok<string>> TypedHandler(Guid viewId, Guid userId, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.UserId = userId;
            command.ViewId = viewId;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(INotificationService notificationService, IHubContext<UserHub> userHub, IPlayerAuthorizationService authorizationService) : BaseHandler<Command, string>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<UserEntity>(request.UserId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!request.IsValid())
            {
                throw new ArgumentException($"Message was NOT sent to user {request.UserId} in view {request.ViewId}");
            }
            var notification = await notificationService.PostToUser(
                request.ViewId,
                request.UserId,
                request,
                cancellationToken);

            if (notification.ToId != request.UserId)
            {
                throw new ForbiddenException($"Message was NOT sent to user {request.UserId} in view {request.ViewId}");
            }

            await userHub.Clients.Group($"{request.ViewId}_{request.UserId}").SendAsync("Reply", notification);
            return $"Message was sent to user {request.UserId} in view {request.ViewId}";
        }
    }
}