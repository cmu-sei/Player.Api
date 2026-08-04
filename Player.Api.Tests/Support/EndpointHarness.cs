// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Tests.Support;

/// <summary>
/// Runs an <see cref="IEndpoint"/>'s <c>RegisterEndpoints</c> against a real
/// <see cref="RouteGroupBuilder"/> and hands back the routes it produced.
/// </summary>
/// <remarks>
/// <see cref="ApiTestHost"/> is a plain container, so it can reach handlers but never their route
/// registrations — which is where the http contract lives: the pattern, the verb, and the name the
/// generated clients are built from. This builds the routing half without Kestrel, an identity
/// provider, or configuration: the endpoints are materialised from the data source rather than served.
/// </remarks>
public sealed class EndpointHarness : IDisposable
{
    private readonly WebApplication _app;
    private readonly RouteGroupBuilder _group;

    public EndpointHarness()
    {
        // Slim builder: routing and DI only, no hosting environment to configure.
        var builder = WebApplication.CreateSlimBuilder();

        // Parameter binding classifies every handler parameter at registration time, and anything it cannot
        // resolve as a service it infers as a body — which minimal APIs reject outright on a GET. These two
        // are the only services the endpoint delegates inject; substitutes are enough, because the delegates
        // are bound and never invoked.
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        builder.Services.AddSingleton(Substitute.For<IIdentityResolver>());

        _app = builder.Build();
        _group = Group();
    }

    // Antiforgery is disabled here because Startup disables it, and for the reason Startup gives: it
    // breaks the form-bound upload endpoints. The rest of what Startup hangs off a group — the
    // ApplicationInstance filter, RequireAuthorization — needs services this container does not have, and
    // none of it reaches the pattern, verb, name or tags under test.
    private RouteGroupBuilder Group()
    {
        var group = _app.MapGroup("/api/");
        group.DisableAntiforgery();

        return group;
    }

    /// <summary>
    /// Registers <paramref name="endpoint"/> and returns its routes, with the tag conventions applied
    /// the way <c>Startup</c> applies them.
    /// </summary>
    public RouteEndpoint[] Register(IEndpoint endpoint, string groupName = "Tests")
    {
        foreach (var builder in endpoint.RegisterEndpoints(_group))
        {
            endpoint.GroupEndpoints(builder, groupName);
        }

        return Materialize();
    }

    /// <summary>Registers a discovered endpoint type; it is <c>Startup</c> that resolves these from DI.</summary>
    public RouteEndpoint[] Register(Type endpointType, string groupName = "Tests") =>
        Register((IEndpoint)Activator.CreateInstance(endpointType), groupName);

    /// <summary>
    /// Registers every endpoint in Player.Api the way <c>Startup</c> does — a fresh <c>/api/</c> group per
    /// namespace, tagged from that namespace's last dot-segment — and returns the whole route table.
    /// </summary>
    public RouteEndpoint[] RegisterAll()
    {
        var groups = DiscoverEndpointTypes()
            .Select(x => (IEndpoint)Activator.CreateInstance(x))
            .GroupBy(x => x.GetType().Namespace);

        foreach (var group in groups)
        {
            var separator = group.Key.LastIndexOf('.');

            // Startup skips any namespace it cannot take a trailing segment from, so this does too.
            if (separator == -1 || separator >= group.Key.Length - 1)
            {
                continue;
            }

            var groupName = group.Key[(separator + 1)..];
            var routeGroup = Group();

            foreach (var endpoint in group)
            {
                foreach (var builder in endpoint.RegisterEndpoints(routeGroup))
                {
                    endpoint.GroupEndpoints(builder, groupName);
                }
            }
        }

        return Materialize();
    }

    /// <summary>Every concrete <see cref="IEndpoint"/> in Player.Api, discovered as <c>Startup</c> discovers them.</summary>
    /// <remarks>Ordered so theories built from this list keep stable case names.</remarks>
    public static Type[] DiscoverEndpointTypes() =>
        [.. typeof(Player.Api.Program).Assembly.GetTypes()
            .Where(t => typeof(IEndpoint).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    // Enumerating the data sources is what runs the conventions and produces the endpoints.
    private RouteEndpoint[] Materialize() =>
        [.. ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()];

    public void Dispose() => ((IDisposable)_app).Dispose();
}

/// <summary>
/// The full Player.Api route table, built once and shared by every test that reads it.
/// </summary>
/// <remarks>
/// Registration is deterministic and the endpoints are immutable once materialised, so paying for the
/// <see cref="EndpointHarness"/> per test would buy nothing.
/// </remarks>
public sealed class EndpointRouteTable : IDisposable
{
    private readonly EndpointHarness _harness = new();

    public EndpointRouteTable() => Routes = _harness.RegisterAll();

    public RouteEndpoint[] Routes { get; }

    public void Dispose() => _harness.Dispose();
}
