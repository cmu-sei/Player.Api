// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Player.Api.Tests.Support;

/// <summary>
/// Routes a request to the database of the test that made it.
/// </summary>
/// <remarks>
/// <para>
/// One host serves the whole run, so the application's own <c>PlayerContext</c> registration cannot
/// be reused: <c>AddEventPublishingDbContextFactory</c> pools its options as a singleton, and the
/// SQLite fallback has no connection string to configure at all — it binds contexts to an open
/// <c>SqliteConnection</c> instance. <see cref="PlayerAppFactory"/> replaces the registration with one
/// that asks this class.
/// </para>
/// <para>
/// Each test registers its session under an id and sends that id as <see cref="HeaderName"/> on every
/// request. A header rather than an <c>AsyncLocal</c>: a lookup that misses then fails loudly and
/// names the request it could not route, where an ambient value that did not flow would silently
/// resolve another test's database.
/// </para>
/// </remarks>
internal static class TestDatabaseScope
{
    public const string HeaderName = "X-Test-Session";

    private static readonly ConcurrentDictionary<Guid, ITestDatabaseSession> _sessions = new();

    public static void Register(Guid id, ITestDatabaseSession session) => _sessions[id] = session;

    public static void Release(Guid id) => _sessions.TryRemove(id, out _);

    /// <summary>
    /// The session belonging to the test that made <paramref name="context"/>'s request.
    /// </summary>
    public static ITestDatabaseSession Resolve(HttpContext context)
    {
        if (context is null)
        {
            throw new InvalidOperationException(
                $"A PlayerContext was resolved outside a request, where no {HeaderName} header can " +
                "say which test database to use. Resolve one from a request scope, or take a context " +
                "from DatabaseTestBase.NewContext.");
        }

        var value = context.Request.Headers[HeaderName].ToString();

        if (!Guid.TryParse(value, out var id))
        {
            throw new InvalidOperationException(
                $"{context.Request.Method} {context.Request.Path} carries no usable {HeaderName} " +
                $"header (found '{value}'). Send requests with a client from ApiTestBase, which sets it.");
        }

        if (!_sessions.TryGetValue(id, out var session))
        {
            throw new InvalidOperationException(
                $"{HeaderName} '{id}' names no registered test database, so the test that owns it has " +
                "already torn down. A request outlived the test that made it; await it before the test " +
                "returns.");
        }

        return session;
    }
}
