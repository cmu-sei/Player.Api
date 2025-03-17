// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.AspNetCore.Authorization;
using Player.Api.ViewModels;
using Microsoft.AspNetCore.SignalR;
using System.Threading;
using System.Threading.Tasks;
using Player.Api.Services;

namespace Player.Api.Hubs
{
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class TeamHub : Hub
    {
        private readonly ITeamService _teamService;
        private readonly INotificationService _notificationService;
        private readonly CancellationToken _ct;

        public TeamHub(ITeamService service, INotificationService notificationService)
        {
            _teamService = service;
            _notificationService = notificationService;
            CancellationTokenSource source = new CancellationTokenSource();
            _ct = source.Token;
        }

        public async Task Join(string idString)
        {
            var id = Guid.Parse(idString);
            var notification = await _notificationService.JoinTeam(id, _ct);
            if (notification.ToId == id)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, idString);
            }
            await Clients.Caller.SendAsync("Reply", notification);
        }

        public async Task Post(string idString, string data)
        {
            var id = Guid.Parse(idString);
            var incomingData = new Notification();
            incomingData.Text = data;
            incomingData.Subject = "Team Notification";
            var notification = await _notificationService.PostToTeam(id, incomingData, _ct);
            if (notification.ToId != id)
            {
                notification.Text = "Message was not sent";
                await Clients.Caller.SendAsync("Reply", notification);
            }
            else
            {
                await Clients.Group(idString).SendAsync("Reply", notification);
            }
        }

        public async Task GetHistory(string idString)
        {
            var id = Guid.Parse(idString);
            var notifications = await _notificationService.GetByTeamAsync(id, _ct);
            await Clients.Caller.SendAsync("History", notifications);
        }

        public async Task Leave(string idString)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, idString);
        }
    }
}
