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
using Player.Api.Options;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models.Webhooks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;

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
                var authOptions = scope.ServiceProvider.GetRequiredService<AuthorizationOptions>();

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
                switch (eventObj.EventType)
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
                            payload.Name = "View Created";
                            payload.Timestamp = DateTime.Now;

                            _logger.LogWarning("Calling callback");
                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                            var auth = await getAuthToken(sub.ClientId, sub.ClientSecret, authOptions.TokenUrl); 
                            resp = await SendJsonPost(jsonPayload, sub.CallbackUri, auth);
                        }
                        break;
                    case EventType.ViewDeleted:
                        foreach (var sub in subs)
                        {
                            var payload = new ViewModels.Webhooks.ViewDeleted();
                            payload.ViewId = eventObj.EffectedEntityId;
                            payload.Name = "View Deleted";
                            payload.Timestamp = DateTime.Now;
                            
                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                            var auth = await getAuthToken(sub.ClientId, sub.ClientSecret, authOptions.TokenUrl); 
                            resp = await SendJsonPost(jsonPayload, sub.CallbackUri, auth);
                        }
                        break;
                    default:
                        _logger.LogDebug("Unknown event type");
                        break;
                }

                // The callback request was accepted, so remove this event from the db
                if (resp != null && resp.StatusCode == HttpStatusCode.Created)
                {
                    var toRemove = await context.PendingEvents
                        .Where(e => e.Id == eventId)
                        .SingleOrDefaultAsync();
                    context.Remove(toRemove);
                    await context.SaveChangesAsync();
                }
                // We got some sort of error, so add this event back to the queue and try again later
                // Add a delay to the new task so it doesn't execute immediately 
                else if (resp != null)
                {
                    _logger.LogWarning("Callback request returned with status code {}", resp.StatusCode);
                    var t = new Task(async id => {
                        await Task.Delay(10000);
                        await ProcessEvent((Guid) id);
                    }, eventId, new CancellationToken());

                    AddEvent(t);
                }
            }
        }

        private async Task<HttpResponseMessage> SendJsonPost(string json, string url, string auth)
        {
            using (var client = _clientFactory.CreateClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth);
                return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            }
        }

        private async Task<string> getAuthToken(string clientId, string clientSecret, string tokenUrl)
        {
            using (var client = _clientFactory.CreateClient())
            {
                // TODO add settings for these?
                var tokenAddr = "http://localhost:5000/connect/token";
                var grantType = "client_credentials";

                var form = new Dictionary<string, string>
                {
                    {"grant_type", grantType},
                    {"client_id", clientId},
                    {"client_secret", clientSecret},
                };

                var resp = await client.PostAsync(tokenAddr, new FormUrlEncodedContent(form));
                var json = await resp.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<BearerToken>(json).AccessToken;
            }
        }

        internal class BearerToken
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; }
        } 
    }
}