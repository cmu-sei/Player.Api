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

namespace Player.Api.Infrastructure.BackgroundServices
{
    public interface IBackgroundWebhookService
    {
        void AddEvent(Guid eventId);
    }

    public class BackgroundWebhookService : BackgroundService, IBackgroundWebhookService
    {
        private readonly ILogger<BackgroundWebhookService> _logger;
        private readonly PlayerContext _context;
        private ActionBlock<Guid> _eventQueue;
        private readonly HttpClient _client;
        

        public BackgroundWebhookService(ILogger<BackgroundWebhookService> logger, PlayerContext context)
        {
            _logger = logger;
            _context = context;
            // We could use dependency injection, but this is the only place where an http client is needed
            _client = new HttpClient();
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
                        payload.ParentId = (Guid) createdView.ViewId;

                        var jsonPayload = JsonSerializer.Serialize(payload);
                        var resp = SendJsonPost(jsonPayload, sub.CallbackUri);
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