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

namespace Player.Api.Features.Teams;

public class SendNotification
{
    [DataContract(Name = "SendTeamNotificationCommand")]
    public class Command : Notification, IRequest<string>
    {
        [JsonIgnore]
        public Guid TeamId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("teams/{id}/notifications", TypedHandler)
                    .WithName("broadcastToTeam")
                    .WithDescription("Broadcasts a Notification to all members of the specified Team.")
                    .WithSummary("Sends a new Team Notification.")
            ];
        }

        async Task<Ok<string>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.TeamId = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(INotificationService notificationService, IHubContext<TeamHub> teamHub, IPlayerAuthorizationService authorizationService) : BaseHandler<Command, string>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!request.IsValid())
            {
                throw new ArgumentException($"Message was NOT sent to team {request.TeamId}");
            }

            var notification = await notificationService.PostToTeam(request.TeamId, request, cancellationToken);

            if (notification.ToId != request.TeamId)
            {
                throw new ForbiddenException($"Message was not sent to team {request.TeamId}");
            }

            await teamHub.Clients.Group(request.TeamId.ToString()).SendAsync("Reply", notification);
            return $"Message was sent to team {request.TeamId}";
        }
    }
}