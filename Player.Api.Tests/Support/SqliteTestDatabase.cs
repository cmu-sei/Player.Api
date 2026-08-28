// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// The fallback provider, used when no Docker daemon is available or when
/// <see cref="DatabaseFixture.ForceSqliteVariable"/> asks for it.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>EnsureCreated()</c> rather than migrations, matching how the application itself treats
/// SQLite: <c>DatabaseExtensions.InitializeDatabase</c> deliberately skips
/// <c>Database.Migrate()</c> for SQLite because only PostgreSQL migrations exist. This means the
/// <c>if (Database.IsNpgsql())</c> branch of <c>PlayerContext.OnModelCreating</c> — snake_case
/// casing and store-generated UUIDs — is not exercised here. Tests that depend on it are marked
/// <see cref="RequiresPostgresAttribute"/>.
/// </para>
/// <para>
/// A session is a file of its own rather than <c>:memory:</c>, because a private in-memory database
/// lives on one connection and every context in the session would have to share that one handle. A
/// test whose subject reads or writes on another thread — a background service polled by
/// <c>WaitUntil</c>, say — then meets <c>SQLite Error 5: database is locked</c> while EF initializes
/// a second context on the busy handle. A file lets each context open its own connection, which is
/// what PostgreSQL does, and write-ahead logging plus a busy timeout make the contention block
/// briefly instead of failing.
/// </para>
/// </remarks>
public sealed class SqliteTestDatabase : ITestDatabase
{
    public TestDatabaseKind Kind => TestDatabaseKind.Sqlite;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<ITestDatabaseSession> BeginSessionAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"player-test-{Guid.NewGuid():N}.db");

        // Pooling off so the file is closed when the session ends and can be deleted; the timeout is
        // what a writer waits for another writer rather than erroring.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString();

        var (services, mediator) = PlayerContextFactory.CreateServices();
        var session = new Session(path, connectionString, services, mediator);

        await using var context = session.CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");

        return session;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class Session(
        string path,
        string connectionString,
        IServiceProvider services,
        IMediator mediator) : ITestDatabaseSession
    {
        public IMediator Mediator { get; } = mediator;

        public PlayerContext CreateContext() => CreateContext(services);

        public PlayerContext CreateContext(IServiceProvider provider) =>
            PlayerContextFactory.CreateContext(builder => builder.UseSqlite(connectionString), provider);

        public ValueTask DisposeAsync()
        {
            // The write-ahead log and its index are siblings of the database file.
            foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
