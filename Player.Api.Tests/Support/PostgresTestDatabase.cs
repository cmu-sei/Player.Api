// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Player.Api.Data.Data;
using Testcontainers.PostgreSql;

namespace Player.Api.Tests.Support;

/// <summary>
/// The default provider: real PostgreSQL in a container, matching production.
/// </summary>
/// <remarks>
/// <para>
/// Migrations are applied once, to a template database. Each test then gets its own database
/// created from that template, which is a file-level copy and so much cheaper than re-running the
/// 34 migrations. This also gives stronger isolation than a shared database would, and keeps
/// SaveChanges behavior identical to the SQLite fallback (see <see cref="ITestDatabaseSession"/>
/// for why transaction-based isolation is not used).
/// </para>
/// <para>
/// The migrations emit <c>CREATE EXTENSION "uuid-ossp"</c> (via
/// <c>ModelBuilderExtensions.AddPostgresUUIDGeneration</c>), which requires superuser. The default
/// <c>postgres</c> user in the official image is superuser, so the container must not be
/// reconfigured to a restricted role.
/// </para>
/// </remarks>
public sealed class PostgresTestDatabase : ITestDatabase
{
    private const string PostgresImage = "postgres:16-alpine";
    private const string TemplateDatabase = "player_template";
    private const string MaintenanceDatabase = "postgres";

    /// <summary>
    /// Production computes this as <c>{AssemblyName}.Migrations.{provider}</c> in
    /// <c>DatabaseExtensions.UseConfiguredDatabase</c>. Without it EF looks for migrations in
    /// <c>PlayerContext</c>'s own assembly and finds none.
    /// </summary>
    private const string MigrationsAssembly = "Player.Api.Migrations.PostgreSQL";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage)
        .WithDatabase(TemplateDatabase)
        .Build();

    private int _databaseCount;

    public TestDatabaseKind Kind => TestDatabaseKind.PostgreSql;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var (services, _) = PlayerContextFactory.CreateServices();

        await using (var context = CreateContextFor(TemplateDatabase, services))
        {
            await context.Database.MigrateAsync();
        }

        // CREATE DATABASE ... TEMPLATE fails while any session is connected to the template, and
        // Npgsql keeps connections alive in its pool after the context is disposed.
        NpgsqlConnection.ClearAllPools();
    }

    public async Task<ITestDatabaseSession> BeginSessionAsync()
    {
        var databaseName = $"player_test_{Interlocked.Increment(ref _databaseCount)}";

        await using (var maintenance = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase)))
        {
            await maintenance.OpenAsync();
            await using var command = maintenance.CreateCommand();
            command.CommandText = $"""CREATE DATABASE "{databaseName}" TEMPLATE "{TemplateDatabase}";""";
            await command.ExecuteNonQueryAsync();
        }

        var (services, mediator) = PlayerContextFactory.CreateServices();

        return new Session(this, databaseName, services, mediator);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private string ConnectionStringFor(string databaseName) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;

    private PlayerContext CreateContextFor(string databaseName, IServiceProvider services) =>
        PlayerContextFactory.CreateContext(
            builder => builder.UseNpgsql(
                ConnectionStringFor(databaseName),
                npgsql => npgsql.MigrationsAssembly(MigrationsAssembly)),
            services);

    private async Task DropDatabaseAsync(string databaseName)
    {
        // Clear only this database's pool. ClearAllPools would churn connections belonging to
        // tests running in parallel.
        await using (var pooled = new NpgsqlConnection(ConnectionStringFor(databaseName)))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await using var maintenance = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase));
        await maintenance.OpenAsync();
        await using var command = maintenance.CreateCommand();
        // FORCE (PostgreSQL 13+) terminates any lingering sessions rather than failing the drop,
        // which keeps teardown from turning into a flaky test failure.
        command.CommandText = $"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Session(
        PostgresTestDatabase database,
        string databaseName,
        IServiceProvider services,
        IMediator mediator) : ITestDatabaseSession
    {
        public IMediator Mediator { get; } = mediator;

        public PlayerContext CreateContext() => database.CreateContextFor(databaseName, services);

        public async ValueTask DisposeAsync() => await database.DropDatabaseAsync(databaseName);
    }
}
