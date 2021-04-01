// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Services;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Events.EventHandlers.Webhooks
{
    public class ViewHandlerBase
    {
        protected readonly ILogger<ViewHandlerBase> _logger;
        protected readonly IBackgroundWebhookService _backgroundService;

        public ViewHandlerBase(ILogger<ViewHandlerBase> logger, IBackgroundWebhookService backgroundService)
        {
            _logger = logger;
            _backgroundService = backgroundService;
        }
    }

    public class ViewCreatedHandler : ViewHandlerBase, INotificationHandler<EntityCreated<ViewEntity>>
    {
        public ViewCreatedHandler(
            ILogger<ViewHandlerBase> logger,
            IBackgroundWebhookService backgroundService) : base(logger, backgroundService) { }

        public async Task Handle(EntityCreated<ViewEntity> notification, CancellationToken ct)
        {
            IWebhookEventPayload payload = new ViewModels.Webhooks.ViewCreated()
            {
                ViewId = notification.Entity.Id,
                ParentId = notification.Entity.ParentViewId
            };

            await _backgroundService.AddEvent(new WebhookEvent(EventType.ViewCreated, payload));
        }
    }

    public class ViewDeletedHandler : ViewHandlerBase, INotificationHandler<EntityDeleted<ViewEntity>>
    {
        public ViewDeletedHandler(
            ILogger<ViewHandlerBase> logger,
            IBackgroundWebhookService backgroundService) : base(logger, backgroundService) { }
        public async Task Handle(EntityDeleted<ViewEntity> notification, CancellationToken ct)
        {
            IWebhookEventPayload payload = new ViewModels.Webhooks.ViewDeleted()
            {
                ViewId = notification.Entity.Id
            };

            await _backgroundService.AddEvent(new WebhookEvent(EventType.ViewDeleted, payload));
        }
    }
}