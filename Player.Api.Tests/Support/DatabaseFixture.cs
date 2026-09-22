// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Player.Api.Tests.Support;

/// <summary>
/// Owns the PostgreSQL database for the whole test run: starts it once and hands out an isolated
/// session per test.
/// </summary>
/// <remarks>
/// PostgreSQL exercises production's actual database, including the
/// <c>if (Database.IsNpgsql())</c> branch of <c>PlayerContext.OnModelCreating</c> and the real
/// migration history. A usable Docker daemon is therefore required to run the suite.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly ITestDatabase _database = new PostgresTestDatabase();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        AnnounceProvider();
    }

    public async Task<ITestDatabaseSession> BeginSessionAsync() => await _database.BeginSessionAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    /// <summary>
    /// Records the provider in the test log so a run shows that it exercised production's database.
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
    private static void AnnounceProvider()
    {
        const string banner =
            "[Player.Api.Tests] database provider: PostgreSQL " +
            "(real migrations, snake_case casing and store-generated UUIDs are covered)";

        Console.WriteLine(banner);
        TestContext.Current.SendDiagnosticMessage(banner);
    }
}
