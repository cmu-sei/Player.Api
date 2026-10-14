// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Player.Api.Data.Data;
using Testcontainers.PostgreSql;

namespace Player.Api.Tests.Support;

/// <summary>
/// Databases that stop at a chosen migration, for the tests of what a later migration does to the data
/// an existing deployment already holds (<c>Migrations/</c>). <see cref="DatabaseFixture"/> hands them out
/// through <see cref="DatabaseFixture.BeginSessionAtMigrationAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The shared <see cref="PostgresTestDatabase{TContext}"/> has one template, already at the latest
/// migration, so these databases come from a PostgreSQL server of their own (the same pinned image),
/// started on the first request. Each migration a test asks for is applied once, from an empty database, to
/// a template of its own; each test then gets a <c>CREATE DATABASE ... TEMPLATE</c> copy of it, as the
/// shared harness does, and drops it when the session is disposed.
/// </para>
/// <para>
/// Migrating down from the latest would test the <c>Down</c> methods rather than the upgrade a deployment
/// runs, so a template is always built upwards from empty.
/// </para>
/// </remarks>
public sealed class UpgradeTestDatabase(string migrationsAssembly) : IAsyncDisposable
{
    private const string Name = "player_upgrade";

    /// <summary>Creating and dropping connect here, never to the database being created or dropped.</summary>
    private const string MaintenanceDatabase = "postgres";

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _templates = new(StringComparer.Ordinal);
    private readonly Lazy<Task<PostgreSqlContainer>> _container = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    private int _databaseCount;

    /// <summary>
    /// A database no other test can see, migrated as far as <paramref name="migrationId"/> and no further.
    /// </summary>
    /// <remarks>
    /// No cancellation token: a template is shared by every test that asks for its migration, and
    /// cancelling the test that asked first would poison it for the others.
    /// </remarks>
    public async Task<UpgradeTestSession> BeginSessionAtMigrationAsync(string migrationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);

        var template = await _templates
            .GetOrAdd(migrationId, id => new Lazy<Task<string>>(() => CreateTemplateAsync(id), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
        var databaseName = NextDatabaseName();

        await ExecuteMaintenanceAsync($"""CREATE DATABASE "{databaseName}" TEMPLATE "{template}";""");

        return new UpgradeTestSession(this, databaseName, await ConnectionStringForAsync(databaseName));
    }

    public async ValueTask DisposeAsync()
    {
        // Nothing to tear down if no test ever asked for an upgrade database.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await (await _container.Value).DisposeAsync();
        }
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        try
        {
            var container = new PostgreSqlBuilder(PostgresTestDatabase<PlayerContext>.PostgresImage).Build();
            await container.StartAsync();
            return container;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start {PostgresTestDatabase<PlayerContext>.PostgresImage} in Docker, which the " +
                "migration tests require. Start Docker and run again; see docs/Testing.md.",
                ex);
        }
    }

    private async Task<string> CreateTemplateAsync(string migrationId)
    {
        var templateName = $"{Name}_template_{Interlocked.Increment(ref _databaseCount)}";

        await ExecuteMaintenanceAsync($"""CREATE DATABASE "{templateName}";""");

        await using (var context = CreateContext(await ConnectionStringForAsync(templateName)))
        {
            await context.GetService<IMigrator>().MigrateAsync(migrationId);
        }

        // Load-bearing, as in the shared harness: CREATE DATABASE ... TEMPLATE fails while any session is
        // connected to the template, and disposing a connection returns it to the pool rather than closing it.
        await using (var pooled = new NpgsqlConnection(await ConnectionStringForAsync(templateName)))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        return templateName;
    }

    private string NextDatabaseName() => $"{Name}_test_{Interlocked.Increment(ref _databaseCount)}";

    private async Task<string> ConnectionStringForAsync(string databaseName) =>
        new NpgsqlConnectionStringBuilder((await _container.Value).GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;

    internal PlayerContext CreateContext(string connectionString)
    {
        var (services, _) = PlayerContextFactory.CreateServices();

        return PlayerContextFactory.CreateContext(
            builder => builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly)),
            services);
    }

    private async Task ExecuteMaintenanceAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(await ConnectionStringForAsync(MaintenanceDatabase));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    internal async Task DropDatabaseAsync(string databaseName)
    {
        await using (var pooled = new NpgsqlConnection(await ConnectionStringForAsync(databaseName)))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await ExecuteMaintenanceAsync($"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""");
    }
}

/// <summary>
/// One test's database, stopped at an earlier migration. Disposing it drops the database.
/// </summary>
/// <remarks>
/// Seed and read it with raw SQL through <c>CreateContext().Database</c> until it is migrated to the
/// latest: the context's model is today's, and its entity sets do not match an earlier schema.
/// </remarks>
public sealed class UpgradeTestSession : IAsyncDisposable
{
    private readonly UpgradeTestDatabase _database;
    private readonly string _connectionString;

    internal UpgradeTestSession(UpgradeTestDatabase database, string databaseName, string connectionString)
    {
        _database = database;
        _connectionString = connectionString;
        DatabaseName = databaseName;
    }

    public string DatabaseName { get; }

    /// <summary>A new context over this session's database. The caller owns it.</summary>
    public PlayerContext CreateContext() => _database.CreateContext(_connectionString);

    /// <summary>
    /// Applies the migrations up to <paramref name="targetMigration"/>, or every remaining migration when
    /// it is <c>null</c>.
    /// </summary>
    public async Task MigrateAsync(string targetMigration = null, CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _database.DropDatabaseAsync(DatabaseName);
}
