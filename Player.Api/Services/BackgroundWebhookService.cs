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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Text;
using Player.Api.ViewModels.Webhooks;
using Player.Api.Data.Data.Models.Webhooks;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using IdentityModel.Client;

namespace Player.Api.Services
{
    public interface IBackgroundWebhookService
    {
        Task AddEvent(WebhookEvent evt);
    }

    public class BackgroundWebhookService : BackgroundService, IBackgroundWebhookService
    {
        private readonly ILogger<BackgroundWebhookService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ActionBlock<WebhookEvent> _eventQueue;
        private readonly ConcurrentDictionary<Guid, ActionBlock<PendingEventEntity>> _sendQueueDict;
        private readonly IOptionsMonitor<AuthorizationOptions> _authOptions;

        public BackgroundWebhookService(
            ILogger<BackgroundWebhookService> logger,
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory clientFactory,
            IOptionsMonitor<AuthorizationOptions> authOptions)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _clientFactory = clientFactory;
            _eventQueue = new ActionBlock<WebhookEvent>(async evt => await ProcessEvent(evt),
                 new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });
            _sendQueueDict = new ConcurrentDictionary<Guid, ActionBlock<PendingEventEntity>>();
            _authOptions = authOptions;
        }

        protected async override Task ExecuteAsync(CancellationToken ct)
        {
            // Add any pending events in the db to send queue
            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<PlayerContext>();
            var events = await context.PendingEvents
                .OrderBy(x => x.Timestamp)
                .ToListAsync(ct);

            foreach (var evt in events)
            {
                await AddPendingEvent(evt);
            }
        }

        public async Task AddEvent(WebhookEvent evt)
        {
            await _eventQueue.SendAsync(evt);
        }

        private async Task AddPendingEvent(PendingEventEntity evt)
        {
            var sendQueue = _sendQueueDict.GetOrAdd(evt.SubscriptionId,
                new ActionBlock<PendingEventEntity>(async evt => await SendEvent(evt.Id),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 }));
            await sendQueue.SendAsync(evt);
        }

        private async Task ProcessEvent(WebhookEvent evt)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlayerContext>();

            // The subscriptions to the current event
            var subscriptions = await context.Webhooks
                .Include(w => w.EventTypes)
                .Where(w => w.EventTypes.Any(et => et.EventType == evt.Type))
                .ToListAsync();

            var pendingEvents = new List<PendingEventEntity>();

            foreach (var subscription in subscriptions)
            {
                var pendingEvent = new PendingEventEntity
                {
                    EventType = evt.Type,
                    SubscriptionId = subscription.Id,
                    Timestamp = evt.Timestamp,
                    Payload = JsonSerializer.Serialize(evt)
                };

                pendingEvents.Add(pendingEvent);
            }

            await context.PendingEvents.AddRangeAsync(pendingEvents);
            await context.SaveChangesAsync();

            pendingEvents.ForEach(async x => await AddPendingEvent(x));
        }

        private async Task SendEvent(Guid eventId)
        {
            bool complete = false;

            while (!complete)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<PlayerContext>();
                    var authOptions = _authOptions.CurrentValue;

                    var evt = await context.PendingEvents
                        .Include(x => x.Subscription)
                        .Where(x => x.Id == eventId)
                        .FirstOrDefaultAsync();

                    if (evt.Subscription != null)
                    {
                        HttpResponseMessage resp = null;

                        try
                        {
                            var auth = await getAuthToken(evt.Subscription.ClientId, evt.Subscription.ClientSecret, authOptions.TokenUrl);
                            resp = await SendJsonPost(evt.Payload, evt.Subscription.CallbackUri, auth);
                        }
                        catch (Exception) { }

                        // The callback request was accepted, so remove this event from the db
                        if (resp != null && resp.StatusCode == HttpStatusCode.Accepted)
                        {
                            // No error occured
                            evt.Subscription.LastError = null;
                            complete = true;
                        }
                        // We got some sort of error, so try again later
                        else
                        {
                            // There was an issue sending the message to the callback endpoint
                            if (resp == null)
                            {
                                evt.Subscription.LastError = "Error sending message to callback endpoint";
                            }
                            // The endpoint returned a status other than 202
                            else
                            {
                                evt.Subscription.LastError = "Callback endpoint returned status code " + resp.StatusCode;
                            }

                            await context.SaveChangesAsync();
                            await Task.Delay(new TimeSpan(0, 1, 0));
                        }
                    }
                    else
                    {
                        complete = true;
                    }

                    if (complete)
                    {
                        context.Remove(evt);
                        await context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception processing Event");
                    await Task.Delay(new TimeSpan(0, 1, 0));
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
                var response = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
                {
                    Address = tokenUrl,
                    ClientId = clientId,
                    ClientSecret = clientSecret
                });

                return response.AccessToken;
            }
        }
    }
}