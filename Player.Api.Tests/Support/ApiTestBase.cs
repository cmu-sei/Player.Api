// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// Base class for tests that drive the application over HTTP: the real routes, the real middleware, the
/// real claims transformer, the real handlers, over a database no other test can see.
/// </summary>
/// <remarks>
/// <para>
/// The HTTP helpers (<c>Client()</c>, <c>ReadAsync</c>, <c>AssertStatus</c>, <c>AssertProblem</c>) and the
/// routing of each request to the test's own database come from the shared
/// <see cref="ApiTestBase{TContext}"/>. This class adds the actors, which are Player's own.
/// </para>
/// <para>
/// The fixtures arrive by constructor injection from the <c>[assembly: AssemblyFixture(...)]</c>
/// declarations in <c>AssemblyFixtures.cs</c>. Derived classes forward both:
/// <c>MyTests(DatabaseFixture fixture, PlayerAppFactory factory) : ApiTestBase(fixture, factory)</c>.
/// </para>
/// </remarks>
public abstract class ApiTestBase(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase<PlayerContext>(fixture, factory)
{
    protected DatabaseFixture Fixture { get; } = fixture;

    protected PlayerAppFactory Factory { get; } = factory;

    /// <summary>
    /// An actor holding every system permission, for the tests that are about what an endpoint does
    /// rather than who may call it. Seeded before each test, so every test has this one user row.
    /// </summary>
    protected TestActor Root { get; private set; } = null!;

    /// <summary>A client that acts as <see cref="Root"/>.</summary>
    protected HttpClient RootClient => Client(Root);

    /// <summary>
    /// Starts describing an actor to seed. <c>await Actor().WithSystemPermissions(...).SeedAsync()</c>.
    /// </summary>
    protected TestActorBuilder Actor() => new(Db, Ct);

    /// <summary>
    /// A client that acts as <paramref name="actor"/>. Cached, so repeated calls share one client and
    /// its headers.
    /// </summary>
    protected HttpClient Client(TestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return ClientFor(actor.Id, actor.Name);
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        Root = await Actor().WithName("Root").WithAllSystemPermissions().SeedAsync();
    }
}
