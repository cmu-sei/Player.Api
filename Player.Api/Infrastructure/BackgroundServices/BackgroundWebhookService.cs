// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Player.Api.Infrastructure.BackgroundServices
{
    public interface IBackgroundWebhookService
    {
        void AddEvent(Guid eventId);
    }

    public class BackgroundWebhookService : BackgroundService, IBackgroundWebhookService
    {
        private ILogger<BackgroundWebhookService> _logger;
        private ActionBlock<Guid> _eventQueue;

        public BackgroundWebhookService(ILogger<BackgroundWebhookService> logger)
        {
            _logger = logger;
        }

        public void AddEvent(Guid eventId)
        {
            // Should this use SendAsync? 
            if (_eventQueue != null)
                _eventQueue.Post(eventId);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _eventQueue = new ActionBlock<Guid>(async eventId => await ProcessEvent(eventId));
        }

        private async Task ProcessEvent(Guid eventId)
        {
            _logger.LogWarning(eventId.ToString());
        }
    }
}