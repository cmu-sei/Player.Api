// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using MediatR;
using Player.Api.Data.Data.Models;

namespace Player.Api.Tests.Support;

/// <summary>
/// Base class for tests that drive the real request handlers, over a real database, through MediatR.
/// </summary>
/// <remarks>
/// A host is built per principal and reused, so several requests as the same user share one container,
/// one <see cref="DatabaseTestBase.Db"/> and one change tracker — which a sequence of production
/// requests would not. Where that matters, re-read through <see cref="DatabaseTestBase.NewContext"/>.
/// </remarks>
public abstract class ApiTestBase(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private readonly Dictionary<ClaimsPrincipal, ApiTestHost> _hosts = [];

    /// <summary>
    /// A principal holding every system permission, for the tests that are about what a handler does
    /// rather than who may call it. Authorization tests build a narrower principal.
    /// </summary>
    protected ClaimsPrincipal Root => _root ??= new ClaimsPrincipalBuilder()
        .WithSystemPermissions(Enum.GetValues<SystemPermission>())
        .Build();

    private ClaimsPrincipal _root;

    /// <summary>The host for <see cref="Root"/>, built on first use.</summary>
    protected ApiTestHost RootHost => HostFor(Root);

    /// <summary>
    /// The host for <paramref name="user"/>. Repeated calls with the same principal return the same
    /// host; <paramref name="configure"/> is honored only on the call that builds it.
    /// </summary>
    protected ApiTestHost HostFor(ClaimsPrincipal user, Action<ApiTestHostOptions> configure = null)
    {
        if (!_hosts.TryGetValue(user, out var host))
        {
            host = ApiTestHost.Create(Db, user, configure);
            _hosts.Add(user, host);
        }

        return host;
    }

    /// <summary>Sends a request as <see cref="Root"/>.</summary>
    protected Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request) =>
        RootHost.Mediator.Send(request, Ct);

    /// <summary>Sends a request as <paramref name="user"/>.</summary>
    protected Task<TResponse> SendAsync<TResponse>(ClaimsPrincipal user, IRequest<TResponse> request) =>
        HostFor(user).Mediator.Send(request, Ct);

    /// <summary>
    /// Sends a request with no response as <see cref="Root"/>. Separate overloads because MediatR's
    /// <c>IRequest</c> and <c>IRequest&lt;T&gt;</c> are unrelated interfaces.
    /// </summary>
    protected Task SendAsync(IRequest request) => RootHost.Mediator.Send(request, Ct);

    /// <summary>Sends a request with no response as <paramref name="user"/>.</summary>
    protected Task SendAsync(ClaimsPrincipal user, IRequest request) =>
        HostFor(user).Mediator.Send(request, Ct);

    /// <summary>
    /// Adds entities and saves. Returns nothing, so a test keeps using the references it already holds.
    /// </summary>
    protected async Task Seed(params object[] entities)
    {
        Db.AddRange(entities);
        await Db.SaveChangesAsync(Ct);
    }

    public override async ValueTask DisposeAsync()
    {
        // Before the base disposes Db, since a container tears down scoped services that hold it.
        foreach (var host in _hosts.Values)
        {
            host.Dispose();
        }

        await base.DisposeAsync();
    }
}
