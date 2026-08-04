// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;

namespace Player.Api.Tests.Support;

/// <summary>
/// Tests for the harness itself. Every other test trusts it, and a harness that quietly does nothing
/// reads as a green suite.
/// </summary>
public class DatabaseHarnessTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>
    /// The unique index on view name is what makes this a real isolation check: if the two
    /// <c>Duplicates_*</c> tests shared a database, whichever ran second would fail.
    /// </summary>
    private const string SharedName = "Isolation Probe";

    [Fact]
    public async Task Saved_entities_survive_a_new_context()
    {
        var view = TestData.View();
        Db.Views.Add(view);
        await Db.SaveChangesAsync(Ct);

        // A second context over the same database, so the read cannot be served from the first
        // context's change tracker.
        await using var context = NewContext();
        var reloaded = await context.Views.SingleOrDefaultAsync(x => x.Id == view.Id, Ct);

        Assert.NotNull(reloaded);
        Assert.Equal(view.Name, reloaded.Name);
    }

    [Fact]
    public async Task Duplicates_across_tests_are_isolated_first()
    {
        Db.Views.Add(TestData.View(SharedName));
        await Db.SaveChangesAsync(Ct);

        Assert.Equal(1, await Db.Views.CountAsync(x => x.Name == SharedName, Ct));
    }

    [Fact]
    public async Task Duplicates_across_tests_are_isolated_second()
    {
        Db.Views.Add(TestData.View(SharedName));
        await Db.SaveChangesAsync(Ct);

        Assert.Equal(1, await Db.Views.CountAsync(x => x.Name == SharedName, Ct));
    }

    [Fact]
    public async Task Seeded_team_roles_are_present()
    {
        // TestData.Team depends on these: TeamEntity.RoleId is a required foreign key.
        var roleIds = await Db.TeamRoles.Select(x => x.Id).ToListAsync(Ct);

        Assert.Contains(TestData.TeamRoles.ViewAdmin, roleIds);
        Assert.Contains(TestData.TeamRoles.Observer, roleIds);
        Assert.Contains(TestData.TeamRoles.ViewMember, roleIds);
    }

    [Fact]
    public async Task Explicit_ids_survive_the_round_trip_under_either_provider()
    {
        // PostgreSQL generates ids in the store and SQLite does not, so TestData assigns them up
        // front. If that ever stopped working, ids would silently become Guid.Empty on SQLite.
        var view = TestData.View();
        var assignedId = view.Id;

        Db.Views.Add(view);
        await Db.SaveChangesAsync(Ct);

        Assert.NotEqual(Guid.Empty, assignedId);
        Assert.Equal(assignedId, view.Id);

        await using var context = NewContext();
        Assert.NotNull(await context.Views.FindAsync([assignedId], Ct));
    }

    /// <summary>
    /// Entity events must publish. This is the behavior that ruled out transaction-per-test isolation:
    /// <c>EntityEventInterceptor</c> defers publishing to <c>TransactionCommitted</c> and discards it
    /// on <c>TransactionRolledBack</c>, so rollback-based isolation would have silently disabled
    /// events under PostgreSQL while leaving them on under SQLite.
    /// </summary>
    [Fact]
    public async Task Saving_publishes_entity_events()
    {
        Db.Views.Add(TestData.View());
        await Db.SaveChangesAsync(Ct);

        // Asserting on INotification rather than object is not cosmetic: IMediator has both
        // Publish(object, ...) and Publish<TNotification>(TNotification, ...), and PlayerContext casts
        // to INotification before publishing, which binds the generic overload. A substitute records
        // the two separately, so matching on object sees no calls at all.
        await Mediator.Received(1).Publish(
            Arg.Is<INotification>(x => x is EntityCreated<ViewEntity>),
            Arg.Any<CancellationToken>());
    }

    [RequiresPostgres]
    public void Uses_the_real_postgres_provider()
    {
        Assert.True(Fixture.IsPostgres);
        Assert.True(Db.Database.IsNpgsql());
    }

    /// <summary>
    /// The PostgreSQL-only half of <c>PlayerContext.OnModelCreating</c>: snake_case naming and
    /// store-generated UUIDs. Neither is reachable on the SQLite fallback, which is what
    /// <see cref="RequiresPostgresAttribute"/> exists for.
    /// </summary>
    [RequiresPostgres]
    public void Applies_postgres_snake_case_naming()
    {
        var entityType = Db.Model.FindEntityType(typeof(ViewMembershipEntity));

        Assert.Equal("view_memberships", entityType.GetTableName());
        Assert.Equal(
            "primary_team_membership_id",
            entityType.FindProperty(nameof(ViewMembershipEntity.PrimaryTeamMembershipId)).GetColumnName());
    }

    [RequiresPostgres]
    public async Task Applies_the_real_migration_history()
    {
        // EnsureCreated leaves no migration history, so this also proves the template database was
        // built by migrations rather than by the model.
        var applied = await Db.Database.GetAppliedMigrationsAsync();

        Assert.NotEmpty(applied);
    }

    /// <summary>
    /// Exercises the SQLite implementation directly rather than through the ambient provider, so the
    /// fallback stays covered on a machine that always resolves PostgreSQL — including CI.
    /// </summary>
    [Fact]
    public async Task The_sqlite_fallback_works_regardless_of_the_active_provider()
    {
        var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        await using var session = await database.BeginSessionAsync();
        var view = TestData.View();

        await using (var context = session.CreateContext())
        {
            context.Views.Add(view);
            await context.SaveChangesAsync(Ct);
            Assert.True(context.Database.IsSqlite());
        }

        await using var reader = session.CreateContext();
        Assert.NotNull(await reader.Views.FindAsync([view.Id], Ct));
    }

    /// <summary>
    /// Whether the fallback enforces foreign keys, which decides whether a test that pins a constraint
    /// violation can run on it. It does, without the connection string asking: no
    /// <c>PRAGMA foreign_keys</c> is sent when the <c>Foreign Keys</c> keyword is absent, and the native
    /// library behind it — <c>SQLitePCLRaw.bundle_e_sqlite3</c> — is compiled with
    /// <c>SQLITE_DEFAULT_FOREIGN_KEYS</c>, which enforces them by default.
    /// </summary>
    /// <remarks>
    /// Recorded because it is not obvious and it is not configured anywhere. The two importer tests that
    /// pin a violation still carry <see cref="RequiresPostgresAttribute"/>, so they do not rest on how
    /// somebody else's native library was built.
    /// </remarks>
    [Fact]
    public async Task The_sqlite_fallback_enforces_foreign_keys()
    {
        var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        await using var session = await database.BeginSessionAsync();
        await using var context = session.CreateContext();

        // A team whose role and view both point at rows that do not exist.
        context.Teams.Add(new TeamEntity
        {
            Id = Guid.NewGuid(),
            Name = "Dangling",
            RoleId = Guid.NewGuid(),
            ViewId = Guid.NewGuid()
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }
}
