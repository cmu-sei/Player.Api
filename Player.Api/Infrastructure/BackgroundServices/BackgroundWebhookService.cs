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
        void AddEvent(Guid eventId);
    }

    public class BackgroundWebhookService : BackgroundService, IBackgroundWebhookService
    {
        private readonly ILogger<BackgroundWebhookService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private ActionBlock<Guid> _eventQueue;
        private readonly HttpClient _client;
        

        public BackgroundWebhookService(ILogger<BackgroundWebhookService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            // We could use dependency injection, but this is the only place where an http client is needed
            // use http client factory
            _client = new HttpClient();
        }

        public void AddEvent(Guid eventId)
        {
            // Should this use SendAsync? 
            if (_eventQueue != null)
            {
                var success = _eventQueue.Post(eventId);
                if (success) _logger.LogWarning("Event added successfully"); 
            }
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _eventQueue = new ActionBlock<Guid>(
                async eventId => await ProcessEvent(eventId));
        }

        private async Task ProcessEvent(Guid eventId)
        {
            // TODO make VM API side of this work
            using (var scope = _scopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<PlayerContext>();

                var eventObj = await _context.PendingEvents
                    .Where(e => e.Id == eventId)
                    .SingleOrDefaultAsync();
                
                var subs = await _context.Webhooks
                    .Include(w => w.EventTypes)
                    .Where(w => w.EventTypes.Any(et => et.EventType == eventObj.EventType))
                    .ToListAsync();

                // For each subcriber to this event, call their callback endpoint
                switch(eventObj.EventType)
                {
                    case EventType.ViewCreated:
                        var createdView = await _context.Views
                            .Where(v => v.Id == eventObj.EffectedEntityId)
                            .SingleOrDefaultAsync();

                        foreach (var sub in subs)
                        {
                            var payload = new ViewModels.Webhooks.ViewCreated();
                            payload.ViewId = createdView.Id;
                            payload.ParentId = createdView.ViewId != null ? (Guid) createdView.ViewId : Guid.Empty;

                            _logger.LogWarning("Calling callback");
                            var jsonPayload = JsonSerializer.Serialize(payload);
                            var resp = await SendJsonPost(jsonPayload, sub.CallbackUri);
                        }
                        break;
                    case EventType.ViewDeleted:
                        foreach (var sub in subs)
                        {
                            var payload = new ViewModels.Webhooks.ViewDeleted();
                            payload.ViewId = eventObj.EffectedEntityId;
                            
                            var jsonPayload = JsonSerializer.Serialize(payload);
                            var resp = SendJsonPost(jsonPayload, sub.CallbackUri);
                        }
                        break;
                    default:
                        _logger.LogDebug("Unknown event type");
                        break;
                }
            }
        }

        private async Task<string> SendJsonPost(string json, string url)
        {
            var request = (HttpWebRequest) WebRequest.Create(url);
            request.ContentType = "application/json";
            request.Method = "POST";

            using (var writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(json);
            }

            var resp = (HttpWebResponse) await request.GetResponseAsync();
            using (var reader = new StreamReader(resp.GetResponseStream()))
            {
                return await reader.ReadToEndAsync();
            }
        }
    }
}