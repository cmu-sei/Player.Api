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
    public class UserHub : Hub
    {
        private readonly INotificationService _notificationService;
        private readonly CancellationToken _ct;

        public UserHub(INotificationService notificationService)
        {
            _notificationService = notificationService;
            CancellationTokenSource source = new CancellationTokenSource();
            _ct = source.Token;
        }

        public async Task Join(string viewString, string userString)
        {
            var userId = Guid.Parse(userString);
            var viewId = Guid.Parse(viewString);
            var notification = await _notificationService.JoinUser(viewId, userId, _ct);
            if (notification.WasSuccess)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupString(viewId, userId));
            }
            await Clients.Caller.SendAsync("Reply", notification);
        }

        public async Task Post(string viewString, string userString, string data)
        {
            var viewId = Guid.Parse(viewString);
            var userId = Guid.Parse(userString);
            var incomingData = new Notification();
            incomingData.Text = data;
            incomingData.Subject = "User Notification";
            var notification = await _notificationService.PostToUser(viewId, userId, incomingData, _ct);
            if (notification.ToId != userId)
            {
                notification.Text = "Message was not sent";
                await Clients.Caller.SendAsync("Reply", notification);
            }
            else
            {
                await Clients.Group(GetGroupString(viewId, userId)).SendAsync("Reply", notification);
            }
        }

        public async Task GetHistory(string viewString, string userString)
        {
            var viewId = Guid.Parse(viewString);
            var userId = Guid.Parse(userString);
            var notifications = await _notificationService.GetByUserAsync(viewId, userId, _ct);
            await Clients.Caller.SendAsync("History", notifications);
        }

        public async Task Leave(string viewString, string userString)
        {
            // A connection is only ever in a group named by valid ids, so there is nothing to leave otherwise.
            if (Guid.TryParse(viewString, out var viewId) && Guid.TryParse(userString, out var userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupString(viewId, userId));
            }
        }

        // Group names are compared as strings, so name the group by the parsed ids: the server sends to
        // "{viewId}_{userId}" in canonical form, whatever casing or braces the caller used.
        private static string GetGroupString(Guid viewId, Guid userId) => $"{viewId}_{userId}";
    }
}
