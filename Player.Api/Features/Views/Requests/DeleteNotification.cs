// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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

public class DeleteNotification
{
    [DataContract(Name = "DeleteViewNotificationCommand")]
    public class Command : IRequest<string>
    {
        [JsonIgnore]
        public Guid ViewId { get; set; }

        [JsonIgnore]
        public int Key { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("views/{id}/notifications/{key}", TypedHandler)
                    .WithName("deleteNotification")
                    .WithDescription("Deletes a Notification in the specified View.")
                    .WithSummary("Deletes a Notification.")
            ];
        }

        async Task<Ok<string>> TypedHandler(Guid id, int key, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Command { ViewId = id, Key = key }, cancellationToken));
        }
    }

    public class Handler(INotificationService notificationService, IHubContext<ViewHub> viewHub, IPlayerAuthorizationService authorizationService) : BaseHandler<Command, string>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            await notificationService.DeleteAsync(request.Key, cancellationToken);
            await viewHub.Clients.Group(request.ViewId.ToString()).SendAsync("Delete", request.Key);
            return $"Notification deleted - {request.Key}";
        }
    }
}