// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Player.Api.Data.Data;
using Player.Api.Hubs;
using Player.Api.Services;

namespace Player.Api.Tests.Support;

/// <summary>
/// Hosts <c>Player.Api</c> in process over <c>TestServer</c>, so that tests drive the real
/// application: the real <c>Startup</c>, the real middleware chain, the real endpoint delegates, the
/// real authorization stack and the real claims transformer.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves the whole run, declared in <c>AssemblyFixtures.cs</c> — starting a host costs
/// about a second, which is affordable once and not once per class. Everything the application
/// registers as a singleton is therefore shared by every test, which is what
/// <see cref="TestConfiguration"/>'s claims-caching entry and <see cref="TestDatabaseScope"/> exist to
/// deal with.
/// </para>
/// <para>
/// <c>Program.CreateWebHostBuilder</c> matches neither convention <c>HostFactoryResolver</c> looks
/// for, so <c>WebApplicationFactory</c> falls back to invoking <c>Program.Main</c> on a background
/// thread with these settings applied. <c>Main</c> then runs to completion, <c>InitializeDatabase</c>
/// included — which is why <c>open-api-only</c> is passed as a host setting as well as as
/// configuration. <c>DeferredHostBuilder</c> turns host settings into <c>--key=value</c> arguments for
/// the entry point, so the value reaches <c>Main</c>'s own gate on that switch and the application
/// skips migrating a database no test would use. It is a production switch, so nothing is substituted
/// to achieve it.
/// </para>
/// <para>
/// Only three things are not the application's own: token validation
/// (<see cref="TestAuthHandler"/>), the context registration (<see cref="TestDatabaseScope"/>), and
/// the collaborators that leave the process.
/// </para>
/// </remarks>
public sealed class PlayerAppFactory : WebApplicationFactory<Program>, ITestHttpHost
{
    /// <summary>
    /// The directory <c>FileService</c> writes uploads to, for tests that assert on what reached disk.
    /// </summary>
    public string FileUploadBasePath => TestConfiguration.FileUploadBasePath;

    /// <summary>
    /// Answers every request the application makes over HTTP. Arrange a url on it, then assert on what
    /// was requested.
    /// </summary>
    /// <remarks>
    /// One handler for the whole run, so a test registers a url of its own and asserts on that url
    /// alone. Registrations are additive and outlive the test that made them, which is why the url has
    /// to be unique rather than merely unregistered elsewhere.
    /// </remarks>
    public StubHttpMessageHandler OutboundHttp { get; } = new();

    /// <summary>
    /// The events the application raised for delivery, for a test that asserts an entity event was
    /// queued.
    /// </summary>
    public WebhookRecorder Webhooks { get; } = new();

    private readonly ConcurrentDictionary<Type, object> _hubs = new();

    private volatile ServiceDescriptor[] _production;

    /// <summary>
    /// What <c>Startup.ConfigureServices</c> registered, before anything this factory replaces. For the
    /// tests that compare the hosted application's composition with <see cref="ApiTestHost"/>'s.
    /// </summary>
    /// <remarks>
    /// Snapshotted at the top of <c>ConfigureTestServices</c>, which runs after <c>Startup</c> and before
    /// the three replacements below, so this is the production composition and not the tested one. Reading
    /// it forces the host to be built, since nothing is registered until then.
    /// </remarks>
    public IReadOnlyList<ServiceDescriptor> ProductionRegistrations
    {
        get
        {
            _ = Services;

            return _production;
        }
    }

    /// <summary>
    /// What the application broadcast through a hub. Held here rather than resolved from the container,
    /// so a test reads the instance the request wrote to.
    /// </summary>
    public HubRecorder<THub> Hub<THub>() where THub : Hub =>
        (HubRecorder<THub>)_hubs.GetOrAdd(typeof(THub), _ => new HubRecorder<THub>());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production, so that UseDeveloperExceptionPage stays off and ExceptionMiddleware answers as
        // it does in a deployment. The status codes and problem bodies tests assert on are its work.
        builder.UseEnvironment("Production");

        // Reaches Program.Main as --open-api-only=true. See the remarks above.
        builder.UseSetting("open-api-only", "true");

        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(TestConfiguration.Values));

        builder.ConfigureTestServices(services =>
        {
            _production = [.. services];

            // Startup does not register provider health checks for its otherwise-unused InMemory
            // context. The route tests need the health-check services, but not another database.
            services.AddHealthChecks();

            AddTestAuthentication(services);
            AddPerTestDatabase(services);
            AddStubbedCollaborators(services);
        });
    }

    /// <summary>
    /// Replaces token validation, and only token validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three hubs name their scheme (<c>[Authorize(AuthenticationSchemes = "Bearer")]</c>), so the
    /// test handler is registered under that name as well as its own, and a real SignalR connection
    /// authenticates through it as a request does. <c>Startup</c>'s <c>AddJwtBearer</c> has already claimed
    /// "Bearer", and <c>AddScheme</c> refuses a duplicate name, so every
    /// <see cref="IConfigureOptions{AuthenticationOptions}"/> it registered (the default scheme and the
    /// bearer scheme) is removed first. This is the standard's Bearer recipe.
    /// </para>
    /// <para>
    /// The default scheme is the test handler's own name, so a request authenticates exactly as before.
    /// </para>
    /// </remarks>
    private static void AddTestAuthentication(IServiceCollection services)
    {
        services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
        services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(JwtBearerDefaults.AuthenticationScheme, null);
    }

    /// <summary>
    /// Points <see cref="PlayerContext"/> at the database of the test making the request, through the
    /// shared <see cref="TestDatabaseScope"/>.
    /// </summary>
    private static void AddPerTestDatabase(IServiceCollection services) =>
        TestDatabaseScope.ReplaceRegistration<PlayerContext>(services);

    /// <summary>
    /// The collaborators that leave the process. Each is something a test can assert against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hub contexts are registered by SignalR as an open generic, which <c>RemoveAll</c> of a closed
    /// type cannot match — a later closed registration wins on resolution, which is what these are.
    /// </para>
    /// <para>
    /// All four are written out rather than substituted. Each is one instance for the whole run, and a
    /// substitute shared by every test loses calls under load: NSubstitute keeps its assertion state per
    /// thread, and <c>TestServer</c> serves requests on the same pool the tests run on. See
    /// <see cref="HubRecorder{THub}"/>.
    /// </para>
    /// </remarks>
    private void AddStubbedCollaborators(IServiceCollection services)
    {
        services.AddSingleton<IHubContext<ViewHub>>(Hub<ViewHub>());
        services.AddSingleton<IHubContext<TeamHub>>(Hub<TeamHub>());
        services.AddSingleton<IHubContext<UserHub>>(Hub<UserHub>());

        services.RemoveAll<IBackgroundWebhookService>();
        services.AddSingleton<IBackgroundWebhookService>(Webhooks);

        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(OutboundHttp));
    }

    private readonly System.Threading.Lock _creating = new();
    private Microsoft.Extensions.Hosting.IHost _host;

    /// <summary>Builds the one host, under a lock, and hands it to every caller.</summary>
    /// <remarks>
    /// <c>WebApplicationFactory.StartServer</c> takes no lock, so two tests asking for their first client at
    /// once would each build a host (each running <c>Program.Main</c>), and the run would continue on two
    /// of them. Every way into the host (<c>CreateClient</c> in any overload, <c>Services</c>,
    /// <c>Server</c>) goes through <c>StartServer</c> to here, so this lock covers them all.
    /// </remarks>
    protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
    {
        lock (_creating)
        {
            return _host ??= base.CreateHost(builder);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        RemoveUploadedFiles();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            RemoveUploadedFiles();
        }
    }

    private static void RemoveUploadedFiles()
    {
        if (Directory.Exists(TestConfiguration.FileUploadBasePath))
        {
            Directory.Delete(TestConfiguration.FileUploadBasePath, recursive: true);
        }
    }
}
