// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Player.Api.Data.Data;
using Player.Api.Extensions;
using Player.Api.Features.Applications;
using Player.Api.Features.Roles;
using Player.Api.Features.TeamRoles;
using Player.Api.Features.Teams;
using Player.Api.Features.Users;
using Player.Api.Features.Views;
using Player.Api.Tests.Support;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// Startup database wiring: which provider the configuration selects, and what
/// <c>InitializeDatabase</c> seeds. Seeding is how a deployment arrives with its roles, users and views
/// already in place, and it runs against a database that may already hold them.
/// </summary>
public class DatabaseExtensionsTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Provider selection ---------------------------------------------------------------------

    [Fact]
    public void DbProvider_defaults_to_Sqlite()
    {
        Assert.Equal("Sqlite", DatabaseExtensions.DbProvider(Configuration()));
    }

    /// <summary>
    /// Trimmed, because the value reaches a switch and an assembly name — a stray space in a config file
    /// would otherwise silently select no provider at all.
    /// </summary>
    [Fact]
    public void DbProvider_trims_the_configured_value()
    {
        Assert.Equal(
            "PostgreSQL",
            DatabaseExtensions.DbProvider(Configuration(new() { ["Database:Provider"] = "  PostgreSQL  " })));
    }

    [Theory]
    [InlineData("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("PostgreSQL", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    public void UseConfiguredDatabase_selects_the_provider_named_in_configuration(
        string provider,
        string expectedProvider)
    {
        var configuration = Configuration(new()
        {
            ["Database:Provider"] = provider,
            [$"ConnectionStrings:{provider}"] = "Data Source=unused;Host=unused;Database=unused"
        });

        var options = new DbContextOptionsBuilder()
            .UseConfiguredDatabase(configuration)
            .Options;

        Assert.Equal(expectedProvider, ProviderAssemblyOf(options));
    }

    /// <summary>
    /// An unrecognized provider is not an error: the builder comes back unconfigured, and the failure
    /// surfaces later as a context with no provider.
    /// </summary>
    [Fact]
    public void UseConfiguredDatabase_configures_nothing_for_an_unknown_provider()
    {
        var options = new DbContextOptionsBuilder()
            .UseConfiguredDatabase(Configuration(new() { ["Database:Provider"] = "Oracle" }))
            .Options;

        Assert.Null(ProviderAssemblyOf(options));
    }

    // ---- Seeding --------------------------------------------------------------------------------

    [Fact]
    public void InitializeDatabase_seeds_permissions_and_roles()
    {
        var host = HostWithSeedData(seed =>
        {
            seed.Permissions =
            [
                new() { Name = "SeededPermission", Description = "From configuration" }
            ];
            seed.Roles =
            [
                new() { Name = "Seeded Role", PermissionNames = ["SeededPermission"] }
            ];
        });

        host.InitializeDatabase();

        using var db = NewContext();
        var permission = db.Permissions.Single(x => x.Name == "SeededPermission");
        Assert.Equal("From configuration", permission.Description);

        var role = db.Roles.Include(x => x.Permissions).Single(x => x.Name == "Seeded Role");
        Assert.Equal(permission.Id, Assert.Single(role.Permissions).PermissionId);
    }

    /// <summary>
    /// A permission name that does not resolve is skipped rather than failing the startup, so one bad
    /// entry cannot stop the application from booting.
    /// </summary>
    [Fact]
    public void InitializeDatabase_skips_an_unknown_permission_name_on_a_role()
    {
        var host = HostWithSeedData(seed =>
            seed.Roles = [new() { Name = "Seeded Role", PermissionNames = ["NoSuchPermission"] }]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Empty(db.Roles.Include(x => x.Permissions).Single(x => x.Name == "Seeded Role").Permissions);
    }

    [Fact]
    public void InitializeDatabase_seeds_team_permissions_and_team_roles()
    {
        var host = HostWithSeedData(seed =>
        {
            seed.TeamPermissions = [new() { Name = "SeededTeamPermission" }];
            seed.TeamRoles =
            [
                new() { Name = "Seeded Team Role", PermissionNames = ["SeededTeamPermission"] }
            ];
        });

        host.InitializeDatabase();

        using var db = NewContext();
        var permission = db.TeamPermissions.Single(x => x.Name == "SeededTeamPermission");
        var role = db.TeamRoles.Include(x => x.Permissions).Single(x => x.Name == "Seeded Team Role");
        Assert.Equal(permission.Id, Assert.Single(role.Permissions).PermissionId);
    }

    /// <summary>
    /// Seeding is by name, and the seeded-by-migration roles are already present — so a configuration
    /// repeating one leaves the stored row alone rather than duplicating it.
    /// </summary>
    [Fact]
    public void InitializeDatabase_leaves_an_existing_role_alone()
    {
        var host = HostWithSeedData(seed =>
            seed.Roles = [new() { Name = "Administrator", AllPermissions = false }]);

        host.InitializeDatabase();

        using var db = NewContext();
        var administrator = Assert.Single(db.Roles.Where(x => x.Name == "Administrator"));
        Assert.True(administrator.AllPermissions);
    }

    [Fact]
    public void InitializeDatabase_seeds_users_with_their_role()
    {
        var id = Guid.NewGuid();
        var host = HostWithSeedData(seed =>
            seed.Users = [new SeedUser { Id = id, Name = "Seeded User", Role = "Administrator" }]);

        host.InitializeDatabase();

        using var db = NewContext();
        var user = db.Users.Single(x => x.Id == id);
        Assert.Equal("Seeded User", user.Name);
        Assert.Equal(TestData.Roles.Administrator, user.RoleId);
    }

    /// <summary>
    /// An unresolvable role name leaves the user without one rather than failing, which is the same
    /// tolerance the role seeding shows.
    /// </summary>
    [Fact]
    public void InitializeDatabase_seeds_a_user_whose_role_does_not_resolve()
    {
        var id = Guid.NewGuid();
        var host = HostWithSeedData(seed =>
            seed.Users = [new SeedUser { Id = id, Name = "Roleless", Role = "No Such Role" }]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Null(db.Users.Single(x => x.Id == id).RoleId);
    }

    /// <summary>
    /// Users are matched by id rather than name, since the id is what the identity provider issues.
    /// </summary>
    [Fact]
    public async Task InitializeDatabase_leaves_an_existing_user_alone()
    {
        var user = TestData.User(name: "Original");
        await Seed(user);

        var host = HostWithSeedData(seed =>
            seed.Users = [new SeedUser { Id = user.Id, Name = "Replacement" }]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Equal("Original", db.Users.Single(x => x.Id == user.Id).Name);
    }

    [Fact]
    public void InitializeDatabase_seeds_webhook_subscriptions()
    {
        var host = HostWithSeedData(seed =>
            seed.Subscriptions =
            [
                new WebhookSubscription
                {
                    Id = Guid.NewGuid(),
                    Name = "Seeded Hook",
                    CallbackUri = "https://example.test/hook",
                    ClientId = "seeded",
                    ClientSecret = "secret"
                }
            ]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Equal("https://example.test/hook", db.Webhooks.Single().CallbackUri);
    }

    [Fact]
    public void InitializeDatabase_seeds_application_templates_by_id()
    {
        var id = Guid.NewGuid();
        var host = HostWithSeedData(seed =>
            seed.ApplicationTemplates =
            [
                new ApplicationTemplate { Id = id, Name = "Seeded Template", Url = "https://example.test" }
            ]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Equal("Seeded Template", db.ApplicationTemplates.Single(x => x.Id == id).Name);
    }

    [Fact]
    public async Task InitializeDatabase_leaves_an_existing_application_template_alone()
    {
        var template = TestData.ApplicationTemplate("Original");
        await Seed(template);

        var host = HostWithSeedData(seed =>
            seed.ApplicationTemplates =
            [
                new ApplicationTemplate { Id = template.Id, Name = "Replacement" }
            ]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Equal("Original", db.ApplicationTemplates.Single(x => x.Id == template.Id).Name);
    }

    /// <summary>
    /// Views are seeded through the same importer the import endpoint uses, so a configured view arrives
    /// with its teams.
    /// </summary>
    [Fact]
    public void InitializeDatabase_seeds_views_through_the_importer()
    {
        var id = Guid.NewGuid();
        var host = HostWithSeedData(seed =>
            seed.Views =
            [
                new ViewExport
                {
                    Id = id,
                    Name = "Seeded View",
                    Teams = [new TeamExport { Id = Guid.NewGuid(), Name = "Blue", RoleName = "View Member" }],
                    Applications = [],
                    Files = []
                }
            ]);

        host.InitializeDatabase();

        using var db = NewContext();
        var view = db.Views.Include(x => x.Teams).Single(x => x.Id == id);
        Assert.Equal("Seeded View", view.Name);
        Assert.Equal("Blue", Assert.Single(view.Teams).Name);
    }

    /// <summary>
    /// A view that is already present is reported as <c>ViewExists</c>, which is the one importer failure
    /// seeding treats as acceptable — otherwise every restart would throw.
    /// </summary>
    [Fact]
    public async Task InitializeDatabase_tolerates_a_view_that_already_exists()
    {
        var view = TestData.View("Already Here");
        await Seed(view);

        var host = HostWithSeedData(seed =>
            seed.Views =
            [
                new ViewExport { Id = view.Id, Name = "Already Here", Teams = [], Applications = [], Files = [] }
            ]);

        host.InitializeDatabase();

        using var db = NewContext();
        Assert.Equal("Already Here", db.Views.Single(x => x.Id == view.Id).Name);
    }

    /// <summary>
    /// Any other importer failure does stop startup, so a deployment does not come up half-seeded and
    /// look healthy.
    /// </summary>
    [Fact]
    public void InitializeDatabase_throws_when_a_seeded_view_cannot_be_imported()
    {
        var host = HostWithSeedData(seed =>
            seed.Views =
            [
                new ViewExport
                {
                    Id = Guid.NewGuid(),
                    Name = "Broken",
                    Teams = [new TeamExport { Id = Guid.NewGuid(), Name = "Blue", RoleName = "No Such Role" }],
                    Applications = [],
                    Files = []
                }
            ]);

        Assert.Throws<Exception>(() => host.InitializeDatabase());
    }

    /// <summary>
    /// Nothing configured means nothing written, which is the default for an upgrade of a live instance.
    /// </summary>
    [Fact]
    public void InitializeDatabase_writes_nothing_without_seed_data()
    {
        HostWithSeedData(_ => { }).InitializeDatabase();

        using var db = NewContext();
        Assert.Empty(db.Views);
        Assert.Empty(db.Users);
        Assert.Empty(db.Webhooks);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static IConfiguration Configuration(Dictionary<string, string> values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    /// <summary>
    /// The assembly of the provider the options configured, or null if none did.
    /// </summary>
    private static string ProviderAssemblyOf(DbContextOptions options) =>
        options.Extensions
            .Where(x => x.Info.IsDatabaseProvider)
            .Select(x => x.GetType().Assembly.GetName().Name)
            .SingleOrDefault();

    /// <summary>
    /// A host whose services are one <see cref="ApiTestHost"/>'s, configured with
    /// <paramref name="configure"/>'s seed data.
    /// </summary>
    private IHost HostWithSeedData(Action<Player.Api.Options.SeedDataOptions> configure) =>
        new SeedHost(HostFor(Root, options => configure(options.SeedData)).Services);

    /// <summary>
    /// <c>InitializeDatabase</c> is an extension on <see cref="IHost"/> but reads only
    /// <see cref="IHost.Services"/>, so this is the whole surface it needs.
    /// </summary>
    private sealed class SeedHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
