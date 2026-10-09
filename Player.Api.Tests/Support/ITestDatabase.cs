// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// The database backing the test suite. Created once per run and asked for a fresh, isolated
/// <see cref="ITestDatabaseSession"/> per test.
/// </summary>
public interface ITestDatabase : IAsyncDisposable
{
    /// <summary>
    /// Starts the PostgreSQL container and migrates the template database.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Hands out an isolated database for a single test.
    /// </summary>
    Task<ITestDatabaseSession> BeginSessionAsync();

    /// <summary>
    /// Hands out an isolated database migrated only as far as <paramref name="migrationId"/>, for a
    /// test of what a later migration does to the data an existing deployment already holds.
    /// </summary>
    /// <remarks>
    /// The database is built from empty rather than copied from the template, so it costs a full run
    /// of the migrations it stops at. Reserve it for upgrade tests.
    /// </remarks>
    Task<IUpgradeTestDatabaseSession> BeginSessionAtMigrationAsync(string migrationId);
}

/// <summary>
/// One test's isolated slice of the database.
/// </summary>
/// <remarks>
/// <para>
/// Isolation deliberately avoids wrapping the test in a rolled-back transaction. The
/// <c>EntityEventInterceptor</c> only publishes entity events on <c>TransactionCommitted</c> when a
/// transaction is in progress, and clears its tracked state on <c>TransactionRolledBack</c> — so
/// transaction-based isolation would silently stop entity events from firing. Each test therefore
/// gets its own PostgreSQL database.
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

    /// <summary>
    /// Creates a context whose <c>ServiceProvider</c> is <paramref name="services"/>, so that
    /// <see cref="PlayerContext.PublishEventsAsync"/> resolves the mediator out of it.
    /// </summary>
    /// <remarks>
    /// This is how a request gets a context: <see cref="PlayerAppFactory"/> passes the request scope,
    /// so entity events reach the application's real handlers rather than
    /// <see cref="Mediator"/>. The parameterless overload passes the session's own provider, whose
    /// mediator is a substitute a test can assert against.
    /// </remarks>
    PlayerContext CreateContext(IServiceProvider services);
}

/// <summary>
/// A session whose database stops at an earlier migration, so a test can seed the schema of that
/// time and then apply the migrations under test.
/// </summary>
/// <remarks>
/// <para>
/// Seed and read it with raw SQL through <c>CreateContext().Database</c>. The context's model is
/// today's, and the entity sets do not match the schema until the database is migrated to the latest.
/// </para>
/// </remarks>
public interface IUpgradeTestDatabaseSession : ITestDatabaseSession
{
    /// <summary>
    /// Applies the migrations up to <paramref name="targetMigration"/>, or every remaining migration
    /// when it is <c>null</c>.
    /// </summary>
    Task MigrateAsync(string targetMigration = null, CancellationToken cancellationToken = default);
}
