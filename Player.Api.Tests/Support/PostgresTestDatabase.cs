// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
/// 34 migrations. This also gives stronger isolation than a shared database would; see
/// <see cref="ITestDatabaseSession"/> for why transaction-based isolation is not used.
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
        var databaseName = NextDatabaseName();

        await ExecuteMaintenanceAsync($"""CREATE DATABASE "{databaseName}" TEMPLATE "{TemplateDatabase}";""");

        var (services, mediator) = PlayerContextFactory.CreateServices();

        return new Session(this, databaseName, services, mediator);
    }

    public async Task<IUpgradeTestDatabaseSession> BeginSessionAtMigrationAsync(string migrationId)
    {
        var databaseName = NextDatabaseName();

        // No template: the template is already at the latest migration.
        await ExecuteMaintenanceAsync($"""CREATE DATABASE "{databaseName}";""");

        var (services, mediator) = PlayerContextFactory.CreateServices();
        var session = new Session(this, databaseName, services, mediator);

        await session.MigrateAsync(migrationId);

        return session;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private string NextDatabaseName() => $"player_test_{Interlocked.Increment(ref _databaseCount)}";

    private async Task ExecuteMaintenanceAsync(string sql)
    {
        await using var maintenance = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase));
        await maintenance.OpenAsync();
        await using var command = maintenance.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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

        // FORCE (PostgreSQL 13+) terminates any lingering sessions rather than failing the drop,
        // which keeps teardown from turning into a flaky test failure.
        await ExecuteMaintenanceAsync($"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""");
    }

    private sealed class Session(
        PostgresTestDatabase database,
        string databaseName,
        IServiceProvider services,
        IMediator mediator) : IUpgradeTestDatabaseSession
    {
        public IMediator Mediator { get; } = mediator;

        public PlayerContext CreateContext() => CreateContext(services);

        public PlayerContext CreateContext(IServiceProvider provider) =>
            database.CreateContextFor(databaseName, provider);

        public async Task MigrateAsync(string targetMigration = null, CancellationToken cancellationToken = default)
        {
            await using var context = CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);
        }

        public async ValueTask DisposeAsync() => await database.DropDatabaseAsync(databaseName);
    }
}
