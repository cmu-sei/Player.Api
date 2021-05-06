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
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;

namespace Player.Api.Infrastructure.BackgroundServices
{
    public interface IBackgroundWebhookService
    {
        Task AddEvent(Task t);
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
            _eventQueue = new ActionBlock<Task>(t => t.Start());
        }

        protected async override Task ExecuteAsync(CancellationToken ct)
        {
            // Add any pending events in the db to event queue
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<PlayerContext>();
                var events = await context.PendingEvents.ToListAsync(ct);

                foreach (var evt in events)
                {
                    var t = new Task(async id => {
                        await ProcessEvent((Guid) id);
                    }, evt.Id, ct);

                    await AddEvent(t);
                }
            }
        }

        public async Task AddEvent(Task t)
        { 
            await _eventQueue.SendAsync(t);
        }

        public async Task ProcessEvent(Guid eventId)
        {
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

                // For each subcriber to this event, call their callback endpoint
                // Only consider it an error if VM API does not receive request
                switch (eventObj.EventType)
                {
                    case EventType.ViewCreated:
                        var createdView = await context.Views
                            .Where(v => v.Id == eventObj.EffectedEntityId)
                            .SingleOrDefaultAsync();

                        foreach (var sub in subs)
                        {
                            HttpResponseMessage resp = null;
                            var payload = new ViewModels.Webhooks.ViewCreated();
                            payload.ViewId = createdView.Id;
                            payload.ParentId = createdView.ViewId != null ? (Guid) createdView.ViewId : Guid.Empty;
                            
                            var webhookEvent = new ViewModels.Webhooks.WebhookEvent();
                            webhookEvent.Name = "View Created";
                            webhookEvent.Timestamp = DateTime.Now;
                            webhookEvent.Payload = payload;

                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(webhookEvent);
                            try
                            {
                                var auth = await getAuthToken(sub.ClientId, sub.ClientSecret, authOptions.TokenUrl); 
                                resp = await SendJsonPost(jsonPayload, sub.CallbackUri, auth);
                            }
                            catch (Exception) {}
                            
                            await handleErrors(resp, sub.Id, eventId);
                        }
                        break;
                    case EventType.ViewDeleted:
                        foreach (var sub in subs)
                        {
                            HttpResponseMessage resp = null;
                            var payload = new ViewModels.Webhooks.ViewDeleted();
                            payload.ViewId = eventObj.EffectedEntityId;

                            var webhookEvent = new ViewModels.Webhooks.WebhookEvent();
                            webhookEvent.Name = "View Deleted";
                            webhookEvent.Timestamp = DateTime.Now;
                            webhookEvent.Payload = payload;
                            
                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(webhookEvent);
                            try
                            {
                                var auth = await getAuthToken(sub.ClientId, sub.ClientSecret, authOptions.TokenUrl); 
                                resp = await SendJsonPost(jsonPayload, sub.CallbackUri, auth);
                            }
                            catch (Exception) {}

                            await handleErrors(resp, sub.Id, eventId);
                        }
                        break;
                    default:
                        _logger.LogDebug("Unknown event type");
                        break;
                }
            }
        }

        private async Task handleErrors(HttpResponseMessage resp, Guid subId, Guid eventId)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<PlayerContext>();

                var sub = await context.Webhooks
                    .Where(w => w.Id == subId)
                    .SingleOrDefaultAsync();
                // The callback request was accepted, so remove this event from the db
                if (resp != null && resp.StatusCode == HttpStatusCode.Accepted)
                {
                    // No error occured
                    sub.LastError = null;

                    var toRemove = await context.PendingEvents
                        .Where(e => e.Id == eventId)
                        .SingleOrDefaultAsync();
                    context.Remove(toRemove);
                    await context.SaveChangesAsync();
                }
                // We got some sort of error, so add this event back to the queue and try again later
                // Add a delay to the new task so it doesn't execute immediately 
                else
                {
                    // There was an issue sending the message to the callback endpoint
                    if (resp == null)
                    {
                        sub.LastError = "Error sending message to callback endpoint";
                    }
                    // The endpoint returned a status other than 202
                    else
                    {
                        sub.LastError = "Callback endpoint returned status code " + resp.StatusCode;
                    }

                    await context.SaveChangesAsync();
                    // Update the error field in db
                    var t = new Task(async id => {
                        await Task.Delay(10000);
                        await ProcessEvent((Guid) id);
                    }, eventId, new CancellationToken());

                    await AddEvent(t);
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
                var grantType = "client_credentials";

                var form = new Dictionary<string, string>
                {
                    {"grant_type", grantType},
                    {"client_id", clientId},
                    {"client_secret", clientSecret},
                };

                var resp = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(form));
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