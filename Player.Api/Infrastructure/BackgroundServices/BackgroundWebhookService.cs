// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models.Webhooks;
using System.Net.Http;
using System.Text.Json;
using System.Net;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace Player.Api.Infrastructure.BackgroundServices
{
    public interface IBackgroundWebhookService
    {
        void AddEvent(Task t);
        Task ProcessEvent(Guid eventId);
    }

    public class BackgroundWebhookService : BackgroundService, IBackgroundWebhookService
    {
        private readonly ILogger<BackgroundWebhookService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _clientFactory;
        private ActionBlock<Task> _eventQueue;
        

        public BackgroundWebhookService(ILogger<BackgroundWebhookService> logger, IServiceScopeFactory scopeFactory, IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _clientFactory = clientFactory;
        }

        public void AddEvent(Task t)
        {
            // Should this use SendAsync? 
            if (_eventQueue != null)
            {
                var success = _eventQueue.Post(t);
                if (success) _logger.LogWarning("Event added successfully"); 
            }
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _eventQueue = new ActionBlock<Task>(t => t.Start());
        }

        public async Task ProcessEvent(Guid eventId)
        {
            // TODO make VM API side of this work
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<PlayerContext>();

                var eventObj = await context.PendingEvents
                    .Where(e => e.Id == eventId)
                    .SingleOrDefaultAsync();
                
                // The subscriptions to the current event
                var subs = await context.Webhooks
                    .Include(w => w.EventTypes)
                    .Where(w => w.EventTypes.Any(et => et.EventType == eventObj.EventType))
                    .ToListAsync();

                HttpResponseMessage resp = null;
                // For each subcriber to this event, call their callback endpoint
                switch(eventObj.EventType)
                {
                    case EventType.ViewCreated:
                        var createdView = await context.Views
                            .Where(v => v.Id == eventObj.EffectedEntityId)
                            .SingleOrDefaultAsync();

                        foreach (var sub in subs)
                        {
                            var payload = new ViewModels.Webhooks.ViewCreated();
                            payload.ViewId = createdView.Id;
                            payload.ParentId = createdView.ViewId != null ? (Guid) createdView.ViewId : Guid.Empty;

                            _logger.LogWarning("Calling callback");
                            var jsonPayload = JsonSerializer.Serialize(payload);
                            resp = await SendJsonPost(jsonPayload, sub.CallbackUri);
                        }
                        break;
                    case EventType.ViewDeleted:
                        foreach (var sub in subs)
                        {
                            var payload = new ViewModels.Webhooks.ViewDeleted();
                            payload.ViewId = eventObj.EffectedEntityId;
                            
                            var jsonPayload = JsonSerializer.Serialize(payload);
                            resp = await SendJsonPost(jsonPayload, sub.CallbackUri);
                        }
                        break;
                    default:
                        _logger.LogDebug("Unknown event type");
                        break;
                }

                // The callback request was accepted, so remove this event from the db
                if (resp != null && resp.StatusCode == HttpStatusCode.Accepted)
                {
                    var toRemove = await context.PendingEvents
                        .Where(e => e.Id == eventId)
                        .SingleOrDefaultAsync();
                    context.Remove(toRemove);
                    await context.SaveChangesAsync();
                }
                // We got some sort of error, so add this event back to the queue and try again later
                // Wait some amount of time before trying again?
                else if (resp != null)
                {
                    var t = new Task(async id => {
                        await Task.Delay(1000);
                        await ProcessEvent((Guid) id);
                    }, eventId, new CancellationToken());

                    AddEvent(t);
                }
            }
        }

        private async Task<HttpResponseMessage> SendJsonPost(string json, string url)
        {
            using (var client = _clientFactory.CreateClient())
            {
                return await client.PostAsync(
                    url,
                    new StringContent(json));
            }
        }
    }
}