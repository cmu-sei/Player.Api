// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// Owns the PostgreSQL database for the whole test run: starts it on first use and hands out an isolated
/// session per test.
/// </summary>
/// <remarks>
/// PostgreSQL exercises production's actual database, including the
/// <c>if (Database.IsNpgsql())</c> branch of <c>PlayerContext.OnModelCreating</c> and the real
/// migration history. A usable Docker daemon is therefore required by every test that takes a database.
/// The mechanics are the shared <see cref="PostgresTestDatabase{TContext}"/>.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime, ITestDatabaseSessionSource<PlayerContext>
{
    private readonly PostgresTestDatabase<PlayerContext> _database = new(new()
    {
        Name = "player",
        TestAssembly = "Player.Api.Tests",
        // Production computes this as {AssemblyName}.Migrations.{provider} in
        // DatabaseExtensions.UseConfiguredDatabase. Without it EF looks for migrations in PlayerContext's
        // own assembly and finds none.
        MigrationsAssembly = "Player.Api.Migrations.PostgreSQL",
        CreateContext = PlayerContextFactory.CreateContext,
        CreateServices = PlayerContextFactory.CreateServices
    });

    /// <summary>Nothing to do here: the container starts on the first request for a session.</summary>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public Task<ITestDatabaseSession<PlayerContext>> BeginSessionAsync() => _database.BeginSessionAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
