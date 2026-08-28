// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Player.Api.Tests.Support;

/// <summary>
/// The connection-scoped state SignalR sets on a hub before invoking a method: who is calling, over which
/// connection, and the proxies the hub sends through.
/// </summary>
/// <remarks>
/// Hubs are only reachable through these three properties, so a hub test is a test of what the hub does to
/// them — which groups it joins and which proxy it sends a message to. The proxies are substitutes rather
/// than a real SignalR pipeline, and <see cref="Sent{T}"/> reads back the argument a
/// <c>SendAsync("Reply", x)</c> extension call turned into a <c>SendCoreAsync</c> call.
/// </remarks>
public sealed class HubHarness
{
    public const string ConnectionId = "connection-1";

    private readonly Dictionary<string, IClientProxy> _groups = [];

    public HubHarness(Guid? userId = null)
    {
        UserId = userId ?? Guid.NewGuid();

        Clients.Caller.Returns(Caller);
        Clients.Group(Arg.Any<string>()).Returns(call => Group(call.Arg<string>()));

        Context.ConnectionId.Returns(ConnectionId);
        Context.Items.Returns(Items);
        Context.User.Returns(new ClaimsPrincipalBuilder().WithUserId(UserId).Build());
    }

    public Guid UserId { get; }

    public IHubCallerClients Clients { get; } = Substitute.For<IHubCallerClients>();

    /// <summary>The connection that invoked the method — where a reply meant for one caller goes.</summary>
    public ISingleClientProxy Caller { get; } = Substitute.For<ISingleClientProxy>();

    public IGroupManager Groups { get; } = Substitute.For<IGroupManager>();

    public HubCallerContext Context { get; } = Substitute.For<HubCallerContext>();

    /// <summary>
    /// Per-connection state that outlives a single hub method — <c>ViewHub</c> keeps its presence id here so
    /// that disconnecting can undo what joining did.
    /// </summary>
    /// <remarks>
    /// SignalR's own dictionary answers null for a key that was never set; a plain
    /// <see cref="Dictionary{TKey, TValue}"/> would throw instead and turn "leave without joining" into a
    /// failure that production does not have.
    /// </remarks>
    public IDictionary<object, object> Items { get; } = new ItemsFeature().Items;

    public T Attach<T>(T hub) where T : Hub
    {
        hub.Clients = Clients;
        hub.Groups = Groups;
        hub.Context = Context;
        return hub;
    }

    /// <summary>One proxy per group name, so a test can assert against the group it expects.</summary>
    public IClientProxy Group(string name)
    {
        if (!_groups.TryGetValue(name, out var proxy))
        {
            proxy = Substitute.For<IClientProxy>();
            _groups[name] = proxy;
        }

        return proxy;
    }

    /// <summary>The single argument sent to <paramref name="method"/>, or a failure if it was not sent once.</summary>
    public static T Sent<T>(IClientProxy proxy, string method)
    {
        var arguments = Assert.Single(Sends(proxy, method));

        return Assert.IsType<T>(Assert.Single(arguments));
    }

    public static void NothingSent(IClientProxy proxy, string method)
    {
        Assert.Empty(Sends(proxy, method));
    }

    private static IEnumerable<object[]> Sends(IClientProxy proxy, string method) =>
        proxy.ReceivedCalls()
            .Where(x => x.GetMethodInfo().Name == nameof(IClientProxy.SendCoreAsync))
            .Select(x => x.GetArguments())
            .Where(x => (string)x[0] == method)
            .Select(x => (object[])x[1]);
}
