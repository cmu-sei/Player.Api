// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Player.Api.Tests.Support;

/// <summary>
/// Base class for tests that drive the application over HTTP: the real routes, the real middleware, the
/// real claims transformer, the real handlers, over a database no other test can see.
/// </summary>
/// <remarks>
/// <para>
/// One host serves the whole run (<see cref="PlayerAppFactory"/>) and each test owns one database
/// (<see cref="DatabaseTestBase.Session"/>). The two are joined by a session id this class registers
/// with <see cref="TestDatabaseScope"/> and puts on every request its clients send.
/// </para>
/// <para>
/// A request runs in its own scope with its own <c>PlayerContext</c>, so what a test reads through
/// <see cref="DatabaseTestBase.Db"/> after acting comes from a change tracker that never saw the write.
/// Re-read through <see cref="DatabaseTestBase.NewContext"/> when asserting on what was stored.
/// </para>
/// <para>
/// The fixtures arrive by constructor injection from the <c>[assembly: AssemblyFixture(...)]</c>
/// declarations in <c>AssemblyFixtures.cs</c>. Derived classes forward both:
/// <c>MyTests(DatabaseFixture fixture, PlayerAppFactory factory) : ApiTestBase(fixture, factory)</c>.
/// </para>
/// </remarks>
public abstract class ApiTestBase(DatabaseFixture fixture, PlayerAppFactory factory)
    : DatabaseTestBase(fixture)
{
    /// <summary>
    /// What the minimal-API endpoints serialize with: web defaults plus the string enum converter
    /// <c>Startup</c> adds to <c>JsonOptions</c>. A DTO's enums are names on the wire, not numbers.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<Guid, HttpClient> _clients = [];
    private readonly Guid _sessionId = Guid.NewGuid();
    private HttpClient _unauthenticated;

    protected PlayerAppFactory Factory { get; } = factory;

    /// <summary>
    /// An actor holding every system permission, for the tests that are about what an endpoint does
    /// rather than who may call it. Seeded before each test, so every test has this one user row.
    /// </summary>
    protected TestActor Root { get; private set; } = null!;

    /// <summary>A client that acts as <see cref="Root"/>.</summary>
    protected HttpClient RootClient => Client(Root);

    /// <summary>
    /// Starts describing an actor to seed. <c>await Actor().WithSystemPermissions(...).SeedAsync()</c>.
    /// </summary>
    protected TestActorBuilder Actor() => new(Db, Ct);

    /// <summary>
    /// A client that acts as <paramref name="actor"/>. Cached, so repeated calls share one client and
    /// its headers.
    /// </summary>
    protected HttpClient Client(TestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!_clients.TryGetValue(actor.Id, out var client))
        {
            client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, actor.Id.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, actor.Name);
            _clients.Add(actor.Id, client);
        }

        return client;
    }

    /// <summary>
    /// A client carrying no identity, whose requests to an <c>/api/</c> route are answered with 401.
    /// </summary>
    protected HttpClient Client() => _unauthenticated ??= CreateClient();

    /// <summary>
    /// Asserts <paramref name="response"/> succeeded and returns its body. The failure message carries
    /// the status and the body, which is where a 500's detail is.
    /// </summary>
    protected static async Task<TValue> ReadAsync<TValue>(HttpResponseMessage response)
    {
        await AssertSuccess(response);

        return await response.Content.ReadFromJsonAsync<TValue>(_json, Ct);
    }

    /// <summary>
    /// Asserts the response status, naming the body when it is not the expected one.
    /// </summary>
    protected static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != expected)
        {
            Assert.Fail(
                $"Expected {(int)expected} {expected} from {Describe(response)}, got " +
                $"{(int)response.StatusCode} {response.StatusCode}: {await Body(response)}");
        }
    }

    /// <summary>
    /// Asserts the response is a <c>ProblemDetails</c> with <paramref name="expected"/> as its status,
    /// which is the shape <c>ExceptionMiddleware</c> answers a handled exception with, and returns it —
    /// a 500's <c>Detail</c> is the exception message, which is what says which failure was reached.
    /// </summary>
    protected static async Task<ProblemDetails> AssertProblem(
        HttpStatusCode expected,
        HttpResponseMessage response)
    {
        await AssertStatus(expected, response);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadFromJsonAsync<ProblemDetails>(_json, Ct);
    }

    private static async Task AssertSuccess(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail(
                $"Expected success from {Describe(response)}, got {(int)response.StatusCode} " +
                $"{response.StatusCode}: {await Body(response)}");
        }
    }

    private static string Describe(HttpResponseMessage response) =>
        $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}";

    private static async Task<string> Body(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);

        return string.IsNullOrWhiteSpace(body) ? "(empty body)" : body;
    }

    private HttpClient CreateClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestDatabaseScope.HeaderName, _sessionId.ToString());

        return client;
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        TestDatabaseScope.Register(_sessionId, Session);

        Root = await Actor().WithName("Root").WithAllSystemPermissions().SeedAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        // Released first: a request that outlives its test then fails naming the header it could not
        // route, rather than reaching a database being torn down underneath it.
        TestDatabaseScope.Release(_sessionId);

        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _unauthenticated?.Dispose();

        await base.DisposeAsync();
    }
}
