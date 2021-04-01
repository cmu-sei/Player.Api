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

namespace Player.Api.Services;

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
    private readonly ConcurrentDictionary<string, CachedToken> _tokenDict;
    private readonly IOptionsMonitor<AuthorizationOptions> _authOptions;
    private const int DelaySecondsInitial = 5;
    private const int DelaySecondsIncrement = 5;
    private const int DelaySecondsMaximum = 60;

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
        _tokenDict = new ConcurrentDictionary<string, CachedToken>();
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
        // Get or create the queue for this event's subscription
        // MaxDegreeOfParallelism = 1 so messages always get sent in order 
        var sendQueue = _sendQueueDict.GetOrAdd(evt.SubscriptionId,
            new ActionBlock<PendingEventEntity>(async evt => await SendEvent(evt.Id),
            new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 }));
        await sendQueue.SendAsync(evt);
    }

    private async Task ProcessEvent(WebhookEvent evt)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Exception processing Event with id = {evt?.Id}");
        }
    }

    private async Task SendEvent(Guid eventId)
    {
        bool complete = false;
        bool sent = false;
        int delaySeconds = DelaySecondsInitial;

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

                if (evt.Subscription != null && !sent)
                {
                    HttpResponseMessage resp = null;
                    string exceptionMessage = null;

                    try
                    {
                        var auth = await getAuthToken(evt.Subscription.ClientId, evt.Subscription.ClientSecret, authOptions.TokenUrl);
                        resp = await SendJsonPost(evt.Payload, evt.Subscription.CallbackUri, auth);
                    }
                    catch (Exception ex)
                    {
                        exceptionMessage = ex.ToString();
                    }

                    // The callback request was accepted, so remove this event from the db
                    if (resp != null && (resp.StatusCode == HttpStatusCode.Accepted || resp.StatusCode == HttpStatusCode.OK))
                    {
                        // No error occured
                        evt.Subscription.LastError = null;
                        complete = true;
                        sent = true;
                    }
                    // We got some sort of error, so try again later
                    else
                    {
                        // There was an issue sending the message to the callback endpoint
                        if (resp == null)
                        {
                            evt.Subscription.LastError = exceptionMessage ?? "Error sending message to callback endpoint";
                        }
                        // The endpoint returned an unexpected status code
                        else
                        {
                            evt.Subscription.LastError = "Callback endpoint returned status code " + resp.StatusCode;

                            // Clear cached token if the endpoint returned an auth error
                            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                this.removeAuthToken(evt.Subscription.ClientId);
                            }
                        }

                        await context.SaveChangesAsync();
                        delaySeconds = await Wait(delaySeconds);
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
                _logger.LogError(ex, $"Exception processing Event with Id = {eventId}. Sent = {sent}");
                delaySeconds = await Wait(delaySeconds);
            }
        }
    }

    /// <summary>
    /// Waits the specified number of seconds and then calculates the next delay
    /// </summary>
    /// <param name="currentDelaySeconds"></param>
    /// <returns>The next number of seconds to delay</returns>
    private async Task<int> Wait(int currentDelaySeconds)
    {
        await Task.Delay(new TimeSpan(0, 0, currentDelaySeconds));
        var nextDelaySeconds = currentDelaySeconds + DelaySecondsIncrement;

        if (nextDelaySeconds >= DelaySecondsMaximum)
        {
            return DelaySecondsMaximum;
        }
        else
        {
            return nextDelaySeconds;
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
        if (_tokenDict.TryGetValue(clientId, out var token))
        {
            if (token.IsValid())
            {
                return token.GetAccessToken();
            }
            else
            {
                this.removeAuthToken(clientId);
            }
        }

        using (var client = _clientFactory.CreateClient())
        {
            var response = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = tokenUrl,
                ClientId = clientId,
                ClientSecret = clientSecret
            });

            if (response.IsError)
            {
                return null;
            }

            var cachedToken = new CachedToken(response);
            _tokenDict.TryAdd(clientId, cachedToken);
            return cachedToken.GetAccessToken();
        }
    }

    private bool removeAuthToken(string clientId)
    {
        return _tokenDict.TryRemove(clientId, out var token);
    }

    private class CachedToken
    {
        public CachedToken(TokenResponse tokenResponse)
        {
            this.AccessToken = tokenResponse.AccessToken;
            this.Expiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        }

        private string AccessToken { get; }
        private DateTime Expiration { get; }

        public string GetAccessToken()
        {
            return this.AccessToken;
        }

        public bool IsValid()
        {
            return DateTime.UtcNow < this.Expiration;
        }
    }
}


