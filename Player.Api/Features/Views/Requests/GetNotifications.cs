using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;

namespace Player.Api.Features.Views;

public class GetNotifications
{
    [DataContract(Name = "GetViewNotificationsCommand")]
    public class Command : Notification, IRequest<Notification[]>
    {
        [JsonIgnore]
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views/{id}/notifications", TypedHandler)
                    .WithName("getAllViewNotifications")
                    .WithDescription("Gets all Notifications for a View.")
                    .WithSummary("Gets all Notifications for a View.")
            ];
        }

        async Task<Ok<Notification[]>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Command { ViewId = id }, cancellationToken));
        }
    }

    public class Handler(INotificationService notificationService, IPlayerAuthorizationService authorizationService) : BaseHandler<Command, Notification[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<Notification[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            return (await notificationService.GetAllViewNotificationsAsync(request.ViewId, cancellationToken)).ToArray();
        }
    }
}