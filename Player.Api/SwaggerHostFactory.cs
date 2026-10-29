// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Player.Api;

/// <summary>
/// The host <c>dotnet swagger tofile</c> reads the OpenAPI document from when the client package is built.
/// </summary>
/// <remarks>
/// Without this class the Swashbuckle CLI builds the host from <see cref="Startup"/> but never starts it,
/// so <see cref="Startup.Configure"/> never runs and every endpoint mapped there (all the
/// <see cref="Infrastructure.Endpoints.IEndpoint"/> groups) is missing from the document. The CLI calls
/// <see cref="CreateHost"/> by convention instead. Starting the host runs <c>Configure</c>; the no-op server
/// keeps it from listening, and <c>open-api-only</c> keeps it away from the database and background services.
/// </remarks>
public static class SwaggerHostFactory
{
    public static IHost CreateHost()
    {
        var host = Program.CreateWebHostBuilder(["--open-api-only=true"])
            .ConfigureServices(services => services.AddSingleton<IServer, NoopServer>())
            .Build();

        host.Start();
        return host;
    }

    private sealed class NoopServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }
}
