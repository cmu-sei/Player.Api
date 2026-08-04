// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// The fallback provider, used when no Docker daemon is available.
/// </summary>
/// <remarks>
/// Uses <c>EnsureCreated()</c> rather than migrations, matching how the application itself treats
/// SQLite: <c>DatabaseExtensions.InitializeDatabase</c> deliberately skips
/// <c>Database.Migrate()</c> for SQLite because only PostgreSQL migrations exist. This means the
/// <c>if (Database.IsNpgsql())</c> branch of <c>PlayerContext.OnModelCreating</c> — snake_case
/// casing and store-generated UUIDs — is not exercised here. Tests that depend on it are marked
/// <see cref="RequiresPostgresAttribute"/>.
/// </remarks>
public sealed class SqliteTestDatabase : ITestDatabase
{
    public TestDatabaseKind Kind => TestDatabaseKind.Sqlite;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<ITestDatabaseSession> BeginSessionAsync()
    {
        // A private, in-memory database that lives exactly as long as its connection.
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var (services, mediator) = PlayerContextFactory.CreateServices();
        var session = new Session(connection, services, mediator);

        using var context = session.CreateContext();
        context.Database.EnsureCreated();

        return Task.FromResult<ITestDatabaseSession>(session);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class Session(
        SqliteConnection connection,
        IServiceProvider services,
        IMediator mediator) : ITestDatabaseSession
    {
        public IMediator Mediator { get; } = mediator;

        public PlayerContext CreateContext() =>
            PlayerContextFactory.CreateContext(builder => builder.UseSqlite(connection), services);

        public async ValueTask DisposeAsync()
        {
            // Disposing the connection discards the in-memory database with it.
            await connection.DisposeAsync();
        }
    }
}
