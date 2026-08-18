// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Options;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Services;

/// <summary>
/// The sender that delivers webhook events to subscribers. An event is written to
/// <c>PendingEvents</c> first and removed only once a subscriber accepts it, so the table is the
/// durability guarantee: nothing may be deleted that was not delivered, and nothing may be delivered twice.
/// </summary>
/// <remarks>
/// <para>
/// The service has no synchronous entry point — work goes onto TPL Dataflow blocks that the constructor
/// starts — so tests observe effects rather than await calls: <see cref="DatabaseTestBase.WaitUntil"/> polls the
/// database or the stubbed subscriber until the effect appears.
/// </para>
/// <para>
/// A failed delivery is retried forever, five seconds apart at first (see issue 30). Tests of failure
/// therefore assert on the error recorded before the first wait and leave the loop running; the sender is
/// given its own contexts through <see cref="ScopeFactory"/> so an abandoned loop cannot touch
/// <see cref="DatabaseTestBase.Db"/> after the test ends.
/// </para>
/// </remarks>
public class BackgroundWebhookServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private const string TokenUri = "https://identity.test/connect/token";
    private const string CallbackUri = "https://subscriber.test/hook";
    private const string OtherCallbackUri = "https://other-subscriber.test/hook";

    private readonly StubHttpMessageHandler _http = new();
    private readonly List<ServiceProvider> _scopeProviders = [];

    // ---- Delivering what is already queued ---------------------------------------------------------

    /// <summary>
    /// The startup drain is what makes the queue durable across restarts: rows left by a previous process
    /// are delivered, with the payload passed through untouched, and removed once accepted.
    /// </summary>
    [Fact]
    public async Task An_event_already_in_the_database_is_delivered_when_the_service_starts()
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id, payload: """{"ViewName":"queued"}"""));
        RespondToToken("token-value");
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "the event to be delivered");
        var delivered = Assert.Single(_http.Sent, x => x.Uri == CallbackUri);
        Assert.Equal(HttpMethod.Post, delivered.Method);
        Assert.Equal("""{"ViewName":"queued"}""", delivered.Body);
        Assert.Equal("application/json", delivered.ContentType);
        Assert.Equal("Bearer token-value", delivered.Headers["Authorization"]);
    }

    /// <summary>
    /// One subscriber's events are delivered oldest first — a subscriber that is told a view was deleted
    /// before it is told it was created cannot make sense of either.
    /// </summary>
    [Fact]
    public async Task Queued_events_are_delivered_oldest_first()
    {
        var webhook = await Subscriber();
        await Seed(
            TestData.PendingEvent(webhook.Id, timestamp: Now.AddMinutes(2), payload: """{"n":3}"""),
            TestData.PendingEvent(webhook.Id, timestamp: Now, payload: """{"n":1}"""),
            TestData.PendingEvent(webhook.Id, timestamp: Now.AddMinutes(1), payload: """{"n":2}"""));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "all three events to be delivered");
        Assert.Equal(
            ["""{"n":1}""", """{"n":2}""", """{"n":3}"""],
            _http.Sent.Where(x => x.Uri == CallbackUri).Select(x => x.Body));
    }

    /// <summary>
    /// Only the subscription an event was queued for is called. Delivering to a subscriber that was not
    /// waiting for the event would leak view activity to whoever else happens to be subscribed.
    /// </summary>
    [Fact]
    public async Task A_subscriber_with_nothing_queued_is_not_called()
    {
        var webhook = await Subscriber();
        var idle = await Subscriber(OtherCallbackUri);
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "the event to be delivered");
        Assert.DoesNotContain(OtherCallbackUri, _http.Requests);
        Assert.Null((await Read(idle)).LastError);
    }

    // ---- Fanning an event out to subscribers --------------------------------------------------------

    /// <summary>
    /// An event is fanned out to every subscription to its type, and to no others — this is the whole point
    /// of the event type list on a subscription.
    /// </summary>
    [Fact]
    public async Task AddEvent_delivers_to_every_subscriber_of_that_event_type()
    {
        await Subscriber(CallbackUri, EventType.ViewCreated);
        await Subscriber(OtherCallbackUri, EventType.ViewCreated);
        await Subscriber("https://uninterested.test/hook", EventType.ViewDeleted);
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);
        _http.RespondWithStatus(OtherCallbackUri, HttpStatusCode.OK);

        using var sender = await Start();
        await sender.AddEvent(ViewCreated("Sales"));

        await WaitUntil(
            async () => _http.Requests.Contains(CallbackUri) && _http.Requests.Contains(OtherCallbackUri),
            "both subscribers to be called");
        await WaitUntil(async () => await PendingCount() == 0, "both events to be removed");
        Assert.DoesNotContain("https://uninterested.test/hook", _http.Requests);
    }

    /// <summary>
    /// With nobody subscribed there is nothing to deliver, so no row is written — the queue does not
    /// accumulate events that will never be sent.
    /// </summary>
    [Fact]
    public async Task AddEvent_with_no_matching_subscription_queues_nothing()
    {
        var subscriber = await Subscriber(CallbackUri, EventType.ViewDeleted);
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();
        await sender.AddEvent(ViewCreated());

        // A second event, this one subscribed, is the barrier. ProcessEvent runs on a block with a degree
        // of parallelism of one, so its delivery cannot be observed until the unmatched event ahead of it
        // has been handled — which a fixed delay could only guess at.
        await sender.AddEvent(new WebhookEvent(EventType.ViewDeleted, new ViewDeleted()));

        // Waiting on Sent rather than Requests, because Sent is what the assertion reads.
        await WaitUntil(
            async () => _http.Sent.Any(x => x.Uri == CallbackUri),
            "the subscribed event to be delivered");

        // One delivery, and an empty queue: the unmatched event neither reached a subscriber nor left a
        // row behind. An empty queue alone would prove nothing, since it is also empty before the wait.
        Assert.Single(_http.Sent, x => x.Uri == CallbackUri);

        // Waited on rather than read. The delivered event's own row is removed after its send returns, so
        // the count observed the moment the delivery appears can still include it. A row the unmatched
        // event left would never be removed, since nothing delivers it, so waiting proves the same thing.
        await WaitUntil(async () => await PendingCount() == 0, "the delivered event's row to be removed");
    }

    /// <summary>
    /// The body a subscriber receives is the whole event — type, timestamp and the serialized payload —
    /// which is the public contract of the webhook.
    /// </summary>
    [Fact]
    public async Task The_delivered_body_is_the_serialized_event()
    {
        await Subscriber(CallbackUri, EventType.ViewCreated);
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();
        var before = DateTime.UtcNow;
        await sender.AddEvent(ViewCreated("Sales"));

        await WaitUntil(async () => _http.Requests.Contains(CallbackUri), "the event to be delivered");
        var body = JsonNode.Parse(Assert.Single(_http.Sent, x => x.Uri == CallbackUri).Body);
        Assert.Equal((int)EventType.ViewCreated, body["Type"].GetValue<int>());

        // Bounded by the run rather than only non-default, since a subscriber orders and expires events by
        // this field: WebhookEvent's constructor stamps DateTime.UtcNow, so a stamp outside the window
        // between constructing the event and observing its delivery is the wrong clock or the wrong event.
        Assert.InRange(body["Timestamp"].GetValue<DateTime>(), before, DateTime.UtcNow);
        var payload = JsonNode.Parse(body["Payload"].GetValue<string>());
        Assert.Equal("Sales", payload["ViewName"].GetValue<string>());

        // Characterizes issue 31: WebhookEvent's id initializer is `new Guid()`, so every event a
        // subscriber sees is identified as all-zeros and cannot be deduplicated. Expect a real id when
        // that is fixed.
        Assert.Equal(Guid.Empty, body["Id"].GetValue<Guid>());
    }

    /// <summary>
    /// The queued row carries what a later process needs to deliver the event without the original in hand.
    /// </summary>
    [Fact]
    public async Task AddEvent_records_the_type_timestamp_and_payload_against_the_subscription()
    {
        var webhook = await Subscriber(CallbackUri, EventType.ViewDeleted);
        // The callback is left unanswered, so the queued row survives to be read.
        RespondToToken();
        using var sender = await Start();

        var evt = new WebhookEvent(EventType.ViewDeleted, new ViewDeleted { ViewId = Guid.NewGuid() });
        await sender.AddEvent(evt);

        await WaitUntil(async () => await PendingCount() == 1, "the event to be queued");
        var queued = await Db.PendingEvents.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(EventType.ViewDeleted, queued.EventType);
        Assert.Equal(webhook.Id, queued.SubscriptionId);
        Assert.Equal(evt.Timestamp, queued.Timestamp, TimeSpan.FromSeconds(1));
        Assert.Contains("ViewId", queued.Payload);
    }

    // ---- Authenticating to the subscriber -----------------------------------------------------------

    /// <summary>
    /// The subscriber's own client credentials are exchanged for a token, so a callback endpoint can
    /// authenticate the caller rather than accept anonymous posts.
    /// </summary>
    [Fact]
    public async Task The_subscription_credentials_are_exchanged_for_the_bearer_token()
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken("issued-token");
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "the event to be delivered");
        var tokenRequest = Assert.Single(_http.Sent, x => x.Uri == TokenUri);
        Assert.Equal(
            $"grant_type=client_credentials&client_id={webhook.ClientId}&client_secret={webhook.ClientSecret}",
            tokenRequest.Body);
        Assert.Equal(
            "Bearer issued-token",
            Assert.Single(_http.Sent, x => x.Uri == CallbackUri).Headers["Authorization"]);
    }

    /// <summary>
    /// A token is cached and reused, so a burst of events costs one round trip to the identity provider
    /// rather than one per event.
    /// </summary>
    [Fact]
    public async Task A_valid_token_is_reused_for_later_events()
    {
        var webhook = await Subscriber();
        await Seed(
            TestData.PendingEvent(webhook.Id, timestamp: Now),
            TestData.PendingEvent(webhook.Id, timestamp: Now.AddMinutes(1)));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "both events to be delivered");
        Assert.Single(_http.Requests, x => x == TokenUri);
        Assert.Equal(2, _http.Requests.Count(x => x == CallbackUri));
    }

    /// <summary>
    /// An expired token is replaced rather than sent, which is what keeps a long-lived sender working past
    /// the token lifetime.
    /// </summary>
    [Fact]
    public async Task An_expired_token_is_requested_again()
    {
        var webhook = await Subscriber();
        await Seed(
            TestData.PendingEvent(webhook.Id, timestamp: Now),
            TestData.PendingEvent(webhook.Id, timestamp: Now.AddMinutes(1)));
        RespondToToken(expiresIn: 0);
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "both events to be delivered");
        Assert.Equal(2, _http.Requests.Count(x => x == TokenUri));
    }

    /// <summary>
    /// Characterizes issue 32: when the identity provider refuses the credentials the event is posted
    /// anyway, with an <c>Authorization</c> header carrying no token, instead of the failure being recorded
    /// against the subscription. Expect a recorded error and no post when that is fixed.
    /// </summary>
    [Fact]
    public async Task A_refused_token_request_posts_the_event_with_no_credentials()
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        _http.Respond(
            TokenUri,
            Encoding.UTF8.GetBytes("""{"error":"invalid_client"}"""),
            "application/json",
            HttpStatusCode.BadRequest);
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "the event to be delivered");
        Assert.Equal("Bearer", Assert.Single(_http.Sent, x => x.Uri == CallbackUri).Headers["Authorization"]);
    }

    /// <summary>
    /// A subscriber that rejects the token is the one case where the cached token must not be reused: the
    /// token may have been revoked, so it is dropped and the next delivery fetches a fresh one.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_subscriber_rejecting_the_token_drops_it_from_the_cache(HttpStatusCode rejection)
    {
        // Both subscriptions use the same client, so the second reuses the first's cached token unless the
        // rejection cleared it.
        var rejecting = await Subscriber(CallbackUri, EventType.ViewCreated);
        await Subscriber(OtherCallbackUri, EventType.ViewDeleted);
        await Seed(TestData.PendingEvent(rejecting.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, rejection);
        _http.RespondWithStatus(OtherCallbackUri, HttpStatusCode.OK);

        using var sender = await Start();
        await WaitUntil(async () => (await Read(rejecting)).LastError != null, "the rejection to be recorded");

        var tokensBeforeSecondDelivery = _http.Requests.Count(x => x == TokenUri);

        // The behaviour is that the cache no longer holds a token, so the next delivery has to fetch one.
        // Counted rather than pinned: the rejected event's own retry is five seconds out, and on a loaded
        // machine it can fetch a token of its own before the second delivery is observed. An exact count
        // then fails as a wrong value, which reads like a regression in token caching rather than a slow
        // run. What must hold either way is that a token was fetched again at all.
        await sender.AddEvent(new WebhookEvent(EventType.ViewDeleted, new ViewDeleted()));
        await WaitUntil(async () => _http.Requests.Contains(OtherCallbackUri), "the second event to be delivered");

        var tokensAfterSecondDelivery = _http.Requests.Count(x => x == TokenUri);
        Assert.True(
            tokensAfterSecondDelivery > tokensBeforeSecondDelivery,
            $"the rejection did not clear the cached token: {tokensBeforeSecondDelivery} token " +
                $"request(s) before the second delivery, {tokensAfterSecondDelivery} after.");
    }

    // ---- Failed deliveries -------------------------------------------------------------------------

    /// <summary>
    /// What counts as delivered: only the two codes that mean the subscriber took responsibility for the
    /// event.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    public async Task An_ok_or_accepted_response_delivers_the_event(int status)
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, (HttpStatusCode)status);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "the event to be removed");
        Assert.Null((await Read(webhook)).LastError);
    }

    /// <summary>
    /// Every other code keeps the row, which is safe but — see issue 30 — never gives up, so a subscriber
    /// answering <c>201</c> or <c>204</c> is retried forever. Those two are the cases worth reading twice:
    /// both are successes to the subscriber that sent them, and neither is treated as one here.
    /// </summary>
    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Any_other_response_keeps_the_event_queued(int status)
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, (HttpStatusCode)status);

        using var sender = await Start();

        await WaitUntil(async () => (await Read(webhook)).LastError != null, "the failure to be recorded");
        Assert.Equal(1, await PendingCount());
    }

    /// <summary>
    /// The status the subscriber returned is recorded on the subscription, which is the only place an
    /// operator can see why a webhook is not arriving.
    /// </summary>
    [Fact]
    public async Task An_unexpected_status_is_recorded_on_the_subscription()
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.InternalServerError);

        using var sender = await Start();

        await WaitUntil(async () => (await Read(webhook)).LastError != null, "the failure to be recorded");
        Assert.Equal(
            "Callback endpoint returned status code InternalServerError",
            (await Read(webhook)).LastError);
    }

    /// <summary>
    /// A subscriber that cannot be reached at all — a wrong host, a refused connection — is the common
    /// misconfiguration, so the transport error itself is recorded rather than a generic message.
    /// </summary>
    [Fact]
    public async Task A_transport_failure_is_recorded_on_the_subscription()
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondByThrowing(CallbackUri, new HttpRequestException("Connection refused"));

        using var sender = await Start();

        await WaitUntil(async () => (await Read(webhook)).LastError != null, "the failure to be recorded");
        Assert.Contains("Connection refused", (await Read(webhook)).LastError);
        Assert.Equal(1, await PendingCount());
    }

    /// <summary>
    /// The retry is deferred rather than immediate, which is what keeps a failing subscriber from being
    /// hammered as fast as the loop can turn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The floor of the first wait is the only part of the schedule a test can observe without spending
    /// it. <c>Wait</c> awaits <c>Task.Delay</c> directly (<c>BackgroundWebhookService.cs:225</c>) with no
    /// clock to substitute, so confirming the documented 5s → 10s → … → 60s would cost three and a half
    /// minutes of wall clock and confirming the cap alone still costs five — see issue 30.
    /// </para>
    /// <para>
    /// One second against a five-second initial wait: a loop that retried without waiting posts again
    /// within milliseconds of recording the error, so the margin is what makes the second post's absence
    /// mean something rather than being a slow machine.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_refused_delivery_is_not_retried_immediately()
    {
        var webhook = await Subscriber();
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.InternalServerError);

        using var sender = await Start();
        await WaitUntil(
            async () => (await Read(webhook)).LastError != null, "the first failure to be recorded");

        await Task.Delay(TimeSpan.FromSeconds(1), Ct);

        Assert.Single(_http.Sent, x => x.Uri == CallbackUri);
        Assert.Equal(1, await PendingCount());
    }

    /// <summary>
    /// A recovered subscription stops reporting an error, so the field means "currently failing" rather
    /// than "has ever failed".
    /// </summary>
    [Fact]
    public async Task A_delivery_clears_an_error_from_an_earlier_attempt()
    {
        var webhook = TestData.Webhook(callbackUri: CallbackUri, eventTypes: [EventType.ViewCreated]);
        webhook.LastError = "Callback endpoint returned status code InternalServerError";
        await Seed(webhook);
        await Seed(TestData.PendingEvent(webhook.Id));
        RespondToToken();
        _http.RespondWithStatus(CallbackUri, HttpStatusCode.OK);

        using var sender = await Start();

        await WaitUntil(async () => await PendingCount() == 0, "the event to be delivered");
        Assert.Null((await Read(webhook)).LastError);
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static DateTime Now => TestData.DefaultDateCreated;

    /// <summary>
    /// A saved subscription. Every one uses the same client credentials, as a deployment's subscriptions
    /// typically do, which is what makes the token cache worth testing.
    /// </summary>
    private async Task<WebhookSubscriptionEntity> Subscriber(
        string callbackUri = CallbackUri,
        EventType eventType = EventType.ViewCreated)
    {
        var webhook = TestData.Webhook(callbackUri: callbackUri, eventTypes: [eventType]);
        await Seed(webhook);
        return webhook;
    }

    /// <summary>
    /// A started sender. Its Dataflow blocks run from construction, so <c>StartAsync</c> only adds the
    /// drain of whatever is already queued — which is the sequence the host produces.
    /// </summary>
    private async Task<BackgroundWebhookService> Start()
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_http));

        var authOptions = Substitute.For<IOptionsMonitor<AuthorizationOptions>>();
        authOptions.CurrentValue.Returns(new AuthorizationOptions { TokenUrl = TokenUri });

        var sender = new BackgroundWebhookService(
            NullLogger<BackgroundWebhookService>.Instance,
            ScopeFactory(),
            clients,
            authOptions);

        await sender.StartAsync(Ct);
        return sender;
    }

    /// <summary>
    /// Hands the sender a fresh <see cref="PlayerContext"/> per scope, as the host's request scopes do.
    /// Sharing the test's own context would not survive the first failed delivery: the retry loop keeps
    /// using its scope factory after the test has finished with it.
    /// </summary>
    private IServiceScopeFactory ScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());

        var provider = services.BuildServiceProvider();
        _scopeProviders.Add(provider);

        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private void RespondToToken(string accessToken = "test-token", int expiresIn = 3600) =>
        _http.Respond(
            TokenUri,
            Encoding.UTF8.GetBytes(
                $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":{{expiresIn}}}"""),
            "application/json");

    private static WebhookEvent ViewCreated(string viewName = "Test View") =>
        new(EventType.ViewCreated, new Player.Api.ViewModels.Webhooks.ViewCreated
        {
            ViewId = Guid.NewGuid(),
            ViewName = viewName
        });

    private Task<int> PendingCount() => Db.PendingEvents.AsNoTracking().CountAsync(Ct);

    private async Task<WebhookSubscriptionEntity> Read(WebhookSubscriptionEntity webhook) =>
        await Db.Webhooks.AsNoTracking().SingleAsync(x => x.Id == webhook.Id, Ct);

    public override async ValueTask DisposeAsync()
    {
        // Before the database goes: an abandoned retry loop then fails on a disposed provider, which it
        // logs and ignores, rather than querying a database that is being dropped.
        foreach (var provider in _scopeProviders)
        {
            await provider.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
