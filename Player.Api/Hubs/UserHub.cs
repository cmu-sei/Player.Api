// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Player.Api.ViewModels;
using Microsoft.AspNetCore.SignalR;
using System.Threading;
using System.Threading.Tasks;
using Player.Api.Services;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Hubs
{
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class UserHub : Hub
    {
        private readonly IUserService _userService;
        private readonly INotificationService _notificationService;
        private readonly CancellationToken _ct;

        public UserHub(IUserService service, INotificationService notificationService)
        {
            _userService = service;
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
