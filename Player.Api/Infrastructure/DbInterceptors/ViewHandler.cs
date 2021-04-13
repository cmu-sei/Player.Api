// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Player.Api.Infrastructure.BackgroundServices;
using MediatR;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;

namespace Player.Api.Infrastructure.DbInterceptors
{
    public class ViewHandlerBase
    {
        protected readonly PlayerContext _context;
        protected readonly ILogger<ViewHandlerBase> _logger;
        protected readonly IBackgroundWebhookService _backgroundService;

        public ViewHandlerBase(PlayerContext context, ILogger<ViewHandlerBase> logger, IBackgroundWebhookService backgroundService)
        {
            _context = context;
            _logger = logger;
            _backgroundService = backgroundService;
        }
    }

    public class ViewCreatedHandler : ViewHandlerBase, INotificationHandler<EntityCreated<ViewEntity>>
    {
        public ViewCreatedHandler(
            PlayerContext context, 
            ILogger<ViewHandlerBase> logger,
            IBackgroundWebhookService backgroundService) : base(context, logger, backgroundService) {}

        public async Task Handle(EntityCreated<ViewEntity> notification, CancellationToken ct)
        {
            // Add pending event to db
            var eventEntity = new PendingEventEntity();
            eventEntity.EventType = EventType.ViewCreated;
            eventEntity.Timestamp = DateTime.Now;
            eventEntity.EffectedEntityId = notification.Entity.Id;

            _context.Add(eventEntity);
            await _context.SaveChangesAsync(ct);

            // Add event to event queue
            Task t = new Task(async eventId => await _backgroundService.ProcessEvent((Guid) eventId), eventEntity.Id, new CancellationToken());

            _logger.LogWarning("Calling AddEvent");
            _backgroundService.AddEvent(t);
        }
    }
}