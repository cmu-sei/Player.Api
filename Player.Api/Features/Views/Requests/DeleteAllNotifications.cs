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
using Player.Api.Services;

namespace Player.Api.Features.Views;

public class DeleteAllNotifications
{
    [DataContract(Name = "DeleteViewNotificationsCommand")]
    public class Command : IRequest<string>
    {
        [JsonIgnore]
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("views/{id}/notifications", TypedHandler)
                    .WithName("deleteViewNotifications")
                    .WithDescription("Deletes all Notifications in the specified View.")
                    .WithSummary("Deletes all Notifications for a view.")
            ];
        }

        async Task<Ok<string>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Command { ViewId = id }, cancellationToken));
        }
    }

    public class Handler(INotificationService notificationService, IHubContext<ViewHub> viewHub, IPlayerAuthorizationService authorizationService) : BaseHandler<Command, string>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            await notificationService.DeleteViewNotificationsAsync(request.ViewId, cancellationToken);
            await viewHub.Clients.Group(request.ViewId.ToString()).SendAsync("Delete", "all");
            return $"Notifications deleted for view {request.ViewId}";
        }
    }
}