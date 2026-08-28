// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Diagnostics;
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
    protected DatabaseFixture Fixture { get; } = fixture;

    /// <summary>
    /// The test's own database. Protected because <see cref="ApiTestBase"/> registers it with
    /// <see cref="TestDatabaseScope"/>, which is how a request reaches it.
    /// </summary>
    protected ITestDatabaseSession Session { get; private set; } = null!;

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
    protected IMediator Mediator => Session.Mediator;

    /// <summary>
    /// Creates an additional context over the same database, for re-reading through a cold change
    /// tracker after a save.
    /// </summary>
    /// <remarks>
    /// The caller owns it: scope it with <c>await using</c>, or hand it to something that disposes it,
    /// such as a scoped service registration. An undisposed context keeps its pooled connection checked
    /// out for the rest of the run, and one PostgreSQL server serves the whole suite.
    /// </remarks>
    protected PlayerContext NewContext() => Session.CreateContext();

    /// <summary>
    /// Adds entities and saves. Returns nothing, so a test keeps using the references it already holds.
    /// </summary>
    protected async Task Seed(params object[] entities)
    {
        Db.AddRange(entities);
        await Db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds, or fails the test naming
    /// <paramref name="what"/> it was waiting for. For the background services, whose work starts in
    /// their constructor and completes nowhere a test can await; prefer a signal, or an effect that
    /// queues behind a later observable one, wherever one exists.
    /// </summary>
    protected static async Task WaitUntil(Func<Task<bool>> condition, string what)
    {
        // A duration, not a count of attempts, so the wait means the same on a loaded CI runner as on a
        // developer machine. Generous because it only bounds a hang: a passing test returns on the first
        // or second poll.
        var budget = TimeSpan.FromSeconds(10);
        var elapsed = Stopwatch.StartNew();

        while (!await condition())
        {
            if (elapsed.Elapsed >= budget)
            {
                Assert.Fail($"Timed out after {budget.TotalSeconds:0.#}s waiting for {what}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), Ct);
        }
    }

    public virtual async ValueTask InitializeAsync()
    {
        Session = await Fixture.BeginSessionAsync();
        Db = NewContext();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Session.DisposeAsync();
    }
}
