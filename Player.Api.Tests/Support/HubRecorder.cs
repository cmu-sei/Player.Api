// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Player.Api.Tests.Support;

/// <summary>
/// Stands in for a hub context and keeps what the application broadcast, so that a test can read the
/// messages sent to one audience.
/// </summary>
/// <remarks>
/// <para>
/// Written out rather than substituted. A hub context registered by <see cref="PlayerAppFactory"/> is
/// one instance for the whole run, and NSubstitute keeps assertion state per thread while
/// <c>TestServer</c> runs requests on the same pool the tests do: a pending <c>Received()</c> from one
/// test can swallow a broadcast made for another, and creating substitutes on several threads at once
/// crosses their bookkeeping. Either way calls go missing, which reads as a broadcast that never
/// happened — a flake that arrives with load rather than with a change.
/// </para>
/// <para>
/// Recording every audience by name also means a test names the group it expects, so a broadcast to
/// the wrong one fails rather than passing on the strength of <c>Clients</c> having been touched.
/// </para>
/// </remarks>
public sealed class HubRecorder<THub> : IHubContext<THub> where THub : Hub
{
    private readonly RecordingClients _clients = new();

    public IHubClients Clients => _clients;

    /// <remarks>
    /// Nothing outside the hubs manages groups, and a hub is given SignalR's own manager rather than
    /// this one, so a call here is a question the harness cannot answer.
    /// </remarks>
    public IGroupManager Groups =>
        throw new NotSupportedException(
            $"Nothing in the application manages groups through IHubContext<{typeof(THub).Name}>. " +
            "Give the recorder a group manager if that has changed.");

    /// <summary>What was broadcast to a group, in the order it was sent.</summary>
    public IReadOnlyList<HubBroadcast> ToGroup(string groupName) => _clients.Recorded($"group:{groupName}");

    /// <summary>What was broadcast to a group, in the order it was sent.</summary>
    public IReadOnlyList<HubBroadcast> ToGroup(Guid groupName) => ToGroup(groupName.ToString());

    /// <summary>
    /// One proxy per audience, kept for the life of the run so that a test reads the same recorder the
    /// request wrote to.
    /// </summary>
    /// <remarks>
    /// The keys are namespaced because a group and a user are named by the same ids — a team's group and
    /// a user's connection would otherwise share a proxy and each see the other's messages.
    /// </remarks>
    private sealed class RecordingClients : IHubClients
    {
        private readonly ConcurrentDictionary<string, RecordingClientProxy> _proxies = new();

        public IClientProxy All => Proxy("all");

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            Proxy($"all-except:{string.Join(',', excludedConnectionIds)}");

        public IClientProxy Client(string connectionId) => Proxy($"client:{connectionId}");

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) =>
            Proxy($"clients:{string.Join(',', connectionIds)}");

        public IClientProxy Group(string groupName) => Proxy($"group:{groupName}");

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            Proxy($"group-except:{groupName}:{string.Join(',', excludedConnectionIds)}");

        public IClientProxy Groups(IReadOnlyList<string> groupNames) =>
            Proxy($"groups:{string.Join(',', groupNames)}");

        public IClientProxy User(string userId) => Proxy($"user:{userId}");

        public IClientProxy Users(IReadOnlyList<string> userIds) =>
            Proxy($"users:{string.Join(',', userIds)}");

        public IReadOnlyList<HubBroadcast> Recorded(string key) =>
            _proxies.TryGetValue(key, out var proxy) ? proxy.Messages : [];

        private RecordingClientProxy Proxy(string key) => _proxies.GetOrAdd(key, _ => new RecordingClientProxy());
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        private readonly ConcurrentQueue<HubBroadcast> _messages = new();

        public IReadOnlyList<HubBroadcast> Messages => [.. _messages];

        public Task SendCoreAsync(
            string method,
            object[] args,
            CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(new HubBroadcast(method, args ?? []));

            return Task.CompletedTask;
        }
    }
}

/// <summary>One message a hub sent: the client method it named, and what it carried.</summary>
public sealed record HubBroadcast(string Method, object[] Arguments)
{
    /// <summary>The single argument the notification and presence broadcasts all send.</summary>
    public object Argument => Arguments.Length == 1
        ? Arguments[0]
        : throw new InvalidOperationException(
            $"'{Method}' was sent with {Arguments.Length} arguments, so name the one you mean.");
}
