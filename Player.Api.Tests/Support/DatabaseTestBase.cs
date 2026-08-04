// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// Base class for tests that need a database. Each test gets its own isolated database from the
/// shared <see cref="DatabaseFixture"/>.
/// </summary>
/// <remarks>
/// The fixture arrives by constructor injection, which xUnit v3 satisfies from the
/// <c>[assembly: AssemblyFixture(typeof(DatabaseFixture))]</c> declaration in
/// <c>AssemblyFixtures.cs</c>. Derived classes forward it: <c>MyTests(DatabaseFixture fixture) :
/// DatabaseTestBase(fixture)</c>.
/// </remarks>
public abstract class DatabaseTestBase(DatabaseFixture fixture) : IAsyncLifetime
{
    private ITestDatabaseSession _session = null!;

    protected DatabaseFixture Fixture { get; } = fixture;

    /// <summary>
    /// The running test's cancellation token. Passing it to awaited calls is what lets the runner
    /// cancel a test that hangs, which matters here more than in a pure unit test: a query blocked on
    /// a PostgreSQL lock would otherwise hold the run open.
    /// </summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The context under test. Created once per test over a database no other test can see.
    /// </summary>
    protected PlayerContext Db { get; private set; } = null!;

    /// <summary>
    /// The substituted mediator that <c>PlayerContext.PublishEventsAsync</c> resolves. Assert on it
    /// to verify published entity events.
    /// </summary>
    protected IMediator Mediator => _session.Mediator;

    /// <summary>
    /// Creates an additional context over the same database, for re-reading through a cold change
    /// tracker after a save.
    /// </summary>
    protected PlayerContext NewContext() => _session.CreateContext();

    public virtual async ValueTask InitializeAsync()
    {
        _session = await Fixture.BeginSessionAsync();
        Db = _session.CreateContext();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _session.DisposeAsync();
    }
}
