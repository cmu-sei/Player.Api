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
using Player.Api.Extensions;

namespace Player.Api.Hubs
{
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class ViewHub : Hub
    {
        private readonly IViewService _viewService;
        private readonly INotificationService _notificationService;
        private readonly CancellationToken _ct;
        private readonly IPresenceService _presenceService;

        public ViewHub(
            IViewService service,
            INotificationService notificationService,
            IPresenceService presenceService)
        {
            _viewService = service;
            _notificationService = notificationService;
            CancellationTokenSource source = new CancellationTokenSource();
            _ct = source.Token;
            _presenceService = presenceService;
        }

        public async Task Join(string idString)
        {
            var id = Guid.Parse(idString);
            var notification = await _notificationService.JoinView(id, _ct);
            if (notification.ToId == id)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, idString);
            }

            var presenceId = await _presenceService.AddConnectionToView(new Guid(idString), Context.User.GetId(), Context.ConnectionId, _ct);
            Context.Items["presence"] = presenceId;

            await Clients.Caller.SendAsync("Reply", notification);
        }

        public async Task Post(string idString, string data)
        {
            var id = Guid.Parse(idString);
            var incomingData = new Notification();
            incomingData.Text = data;
            incomingData.Subject = "View Notification";
            var notification = await _notificationService.PostToView(id, incomingData, _ct);
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
            var notifications = await _notificationService.GetByViewAsync(id, _ct);
            await Clients.Caller.SendAsync("History", notifications);
        }

        public async Task Leave(string idString)
        {
            // remove connection from presence service
            var presenceId = Context.Items["presenceId"];

            if (presenceId != null)
            {
                await _presenceService.RemoveConnectionFromView((Guid)presenceId, Context.User.GetId(), Context.ConnectionId, _ct);
                Context.Items.Remove("presenceId");
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, idString);
        }

        public override async Task OnDisconnectedAsync(Exception ex)
        {
            // remove connection from presence service
            var presenceId = Context.Items["presenceId"];

            if (presenceId != null)
            {
                await _presenceService.RemoveConnectionFromView((Guid)presenceId, Context.User.GetId(), Context.ConnectionId, _ct);
                Context.Items.Remove("presenceId");
            }

            await base.OnDisconnectedAsync(ex);
        }

        // return user statuses for accessible teams and subscribe to updates
        public async Task<IEnumerable<ViewPresence>> JoinPresence(Guid viewId)
        {
            var group = await _presenceService.GetGroupByViewId(viewId);
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
            return await _presenceService.GetPresenceByViewId(viewId);
        }

        public async Task LeavePresence(Guid viewId)
        {
            var group = await _presenceService.GetGroupByViewId(viewId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }
    }
}
