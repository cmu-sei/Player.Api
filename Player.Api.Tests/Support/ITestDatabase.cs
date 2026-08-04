// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// Which provider is backing the current test run.
/// </summary>
public enum TestDatabaseKind
{
    PostgreSql,
    Sqlite
}

/// <summary>
/// A database the test suite can run against. Created once per run and asked for a fresh,
/// isolated <see cref="ITestDatabaseSession"/> per test.
/// </summary>
public interface ITestDatabase : IAsyncDisposable
{
    TestDatabaseKind Kind { get; }

    /// <summary>
    /// Prepares the database so sessions can be handed out: starts the container and migrates
    /// the template (PostgreSQL), or verifies the provider is usable (SQLite).
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Hands out an isolated database for a single test.
    /// </summary>
    Task<ITestDatabaseSession> BeginSessionAsync();
}

/// <summary>
/// One test's isolated slice of the database.
/// </summary>
/// <remarks>
/// <para>
/// Isolation deliberately avoids wrapping the test in a rolled-back transaction. The
/// <c>EntityEventInterceptor</c> only publishes entity events on <c>TransactionCommitted</c> when a
/// transaction is in progress, and clears its tracked state on <c>TransactionRolledBack</c> — so
/// transaction-based isolation would silently stop entity events from firing, and only on
/// PostgreSQL. Both providers therefore give each test its own database instead, which keeps
/// SaveChanges behavior identical across providers.
/// </para>
/// </remarks>
public interface ITestDatabaseSession : IAsyncDisposable
{
    /// <summary>
    /// The substituted <see cref="IMediator"/> that <see cref="PlayerContext.PublishEventsAsync"/>
    /// resolves. Assert against it to verify published entity events.
    /// </summary>
    IMediator Mediator { get; }

    /// <summary>
    /// Creates a new context over this session's database. Call more than once when a test needs
    /// to re-read through a cold change tracker.
    /// </summary>
    PlayerContext CreateContext();
}
