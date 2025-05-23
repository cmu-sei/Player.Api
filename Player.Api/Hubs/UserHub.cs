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
            if (notification.ToId == userId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, getGroupString(viewString, userString));
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
                await Clients.Group(getGroupString(viewString, userString)).SendAsync("Reply", notification);
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
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, getGroupString(viewString, userString));
        }

        private string getGroupString(string viewString, string userString)
        {
            return String.Format("{0}_{1}", viewString, userString);
        }
    }
}
