// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using DotNet.Testcontainers.Configurations;

namespace Player.Api.Tests.Support;

/// <summary>
/// Owns the database for the whole test run: decides the provider, starts it once, and hands out
/// an isolated session per test.
/// </summary>
/// <remarks>
/// PostgreSQL is the default because it exercises production's actual database, including the
/// <c>if (Database.IsNpgsql())</c> branch of <c>PlayerContext.OnModelCreating</c> and the real
/// migration history. SQLite is a fallback so a contributor without Docker still gets coverage
/// rather than a wall of skips.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Set this in CI. When set, an unavailable Docker daemon is a hard failure instead of a
    /// silent fallback to SQLite — otherwise a CI run could go green having never touched
    /// PostgreSQL.
    /// </summary>
    public const string RequirePostgresVariable = "PLAYER_TESTS_REQUIRE_POSTGRES";

    private static TestDatabaseKind? _resolvedKind;

    private ITestDatabase _database = null!;

    public TestDatabaseKind Kind => _database.Kind;

    /// <summary>
    /// True when the active provider is PostgreSQL. <see cref="RequiresPostgresAttribute"/> tests
    /// skip when this is false.
    /// </summary>
    public bool IsPostgres => Kind == TestDatabaseKind.PostgreSql;

    /// <summary>
    /// The static form of <see cref="IsPostgres"/>, which is what xUnit's <c>SkipUnless</c> needs —
    /// it reads a static property, and there is no instance to hand it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static mutable state is normally a smell, but the provider genuinely is global here: one
    /// database is resolved per assembly, by the one fixture. xUnit evaluates skip predicates at
    /// test-execution time, and the assembly fixture is initialized before the first test runs, so
    /// this is populated by the time it is read.
    /// </para>
    /// <para>
    /// This deliberately reports the <em>resolved</em> provider rather than
    /// <see cref="DockerAvailable"/>. The probe can say Docker is reachable when starting the
    /// container still fails; reading the probe would leave PostgreSQL-only tests running against
    /// the SQLite fallback and failing for the wrong reason.
    /// </para>
    /// </remarks>
    public static bool PostgresActive => _resolvedKind == TestDatabaseKind.PostgreSql;

    public static bool PostgresRequired =>
        Environment.GetEnvironmentVariable(RequirePostgresVariable)
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Testcontainers resolves the Docker endpoint from a chain of providers and leaves this null
    /// when none applies, which is a cheaper first check than attempting a container start.
    /// </summary>
    private static bool DockerAvailable => TestcontainersSettings.OS.DockerEndpointAuthConfig is not null;

    public async ValueTask InitializeAsync()
    {
        _database = await ResolveDatabaseAsync();
        _resolvedKind = _database.Kind;
        AnnounceProvider(_database.Kind);
    }

    public async Task<ITestDatabaseSession> BeginSessionAsync() => await _database.BeginSessionAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    private static async Task<ITestDatabase> ResolveDatabaseAsync()
    {
        var requirePostgres = PostgresRequired;

        if (!DockerAvailable)
        {
            if (requirePostgres)
            {
                throw new InvalidOperationException(
                    $"{RequirePostgresVariable} is set but no Docker daemon was found, so the " +
                    "PostgreSQL tests cannot run. Falling back to SQLite here would report a " +
                    "green run that never exercised production's database. Start Docker, or " +
                    $"unset {RequirePostgresVariable} to allow the SQLite fallback.");
            }

            return new SqliteTestDatabase();
        }

        var postgres = new PostgresTestDatabase();

        try
        {
            await postgres.InitializeAsync();
            return postgres;
        }
        catch (Exception ex) when (!requirePostgres)
        {
            // The probe said Docker was reachable but starting the container failed anyway.
            // Fall back rather than failing a local run outright.
            await postgres.DisposeAsync();
            Console.WriteLine($"[Player.Api.Tests] PostgreSQL startup failed, falling back to SQLite: {ex.Message}");

            return new SqliteTestDatabase();
        }
    }

    /// <summary>
    /// Records which provider the run resolved. Which provider ran decides what the run actually
    /// proved, so this needs to survive into a CI log.
    /// </summary>
    /// <remarks>
    /// Sent as an xUnit diagnostic message, not just written to the console. An assembly fixture
    /// initializes before any test exists to attach output to, and the VSTest bridge that
    /// <c>dotnet test</c> uses discards the test host's plain stdout at every verbosity, so a
    /// <see cref="Console.WriteLine"/> alone reaches a direct <c>dotnet run</c> of the suite but never
    /// a <c>dotnet test</c> log. The diagnostic sink does get surfaced, with
    /// <c>-- xUnit.DiagnosticMessages=true</c>. Both channels are used so the banner shows up either
    /// way round.
    /// </remarks>
    private static void AnnounceProvider(TestDatabaseKind kind)
    {
        var detail = kind == TestDatabaseKind.PostgreSql
            ? "real migrations, snake_case casing and store-generated UUIDs are covered"
            : "FALLBACK: migrations, snake_case casing and store-generated UUIDs are NOT covered";

        var banner = $"[Player.Api.Tests] database provider: {kind} ({detail})";

        Console.WriteLine(banner);
        TestContext.Current.SendDiagnosticMessage(banner);
    }
}
