// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Data.Data.Models;

namespace Player.Api.Tests.Support;

/// <summary>
/// Base class for tests that resolve a service out of a container built for one principal.
/// </summary>
/// <remarks>
/// <para>
/// A host is built per principal and reused, so services resolved as the same user share one container,
/// one <see cref="DatabaseTestBase.Db"/> and one change tracker — which a sequence of production
/// requests would not. Where that matters, re-read through <see cref="DatabaseTestBase.NewContext"/>.
/// </para>
/// <para>
/// A test that sends a request belongs on <see cref="ApiTestBase"/> instead, which drives the
/// application over HTTP so that the endpoint, the middleware and the claims transformer are the real
/// ones. This class is for the tests that construct a service with per-test options.
/// </para>
/// </remarks>
public abstract class ServiceTestBase(DatabaseFixture fixture) : DatabaseTestBase(fixture)
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
    /// The host for <paramref name="user"/>. Repeated calls with the same principal return the same host,
    /// and asking for options against a principal already hosted throws.
    /// </summary>
    /// <remarks>
    /// A host is built once per principal, so a second <paramref name="configure"/> for the same one has
    /// nothing to configure. Handing back the first host silently would leave the test asserting against a
    /// configuration it did not set — passing or failing for a reason that is nowhere in the test. Throwing
    /// names it instead. Build a principal per configuration; <see cref="ClaimsPrincipalBuilder"/> produces
    /// a new instance each time, and the dictionary keys on the instance.
    /// </remarks>
    protected ApiTestHost HostFor(ClaimsPrincipal user, Action<ApiTestHostOptions> configure = null)
    {
        if (_hosts.TryGetValue(user, out var host))
        {
            if (configure != null)
            {
                throw new InvalidOperationException(
                    "A host for this principal has already been built, so these options would be " +
                    "ignored. Build a distinct principal for each configuration.");
            }

            return host;
        }

        host = ApiTestHost.Create(Db, user, configure);
        _hosts.Add(user, host);

        return host;
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
