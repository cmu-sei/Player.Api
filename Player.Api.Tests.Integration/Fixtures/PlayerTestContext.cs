// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Player.Api.Data.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace Player.Api.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory backed by a Testcontainers PostgreSQL instance.
/// Replaces the production database registration with one pointing at the container,
/// and swaps authentication for a test scheme that bypasses real OIDC.
/// </summary>
public class PlayerTestContext : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("player_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override database provider to PostgreSQL via in-memory config
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSQL",
                ["Database:AutoMigrate"] = "false",
                ["ConnectionStrings:PostgreSQL"] = _postgres.GetConnectionString(),
                ["Authorization:Authority"] = "https://localhost",
                ["Authorization:AuthorizationScope"] = "player-api",
                ["Authorization:ClientId"] = "test-client",
                ["CorsPolicy:Origins:0"] = "http://localhost",
                // Disable background services for tests
                ["open-api-only"] = "true"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove any existing DbContextFactory and DbContext registrations
            // to replace with our Testcontainers PostgreSQL
            services.RemoveAll<IDbContextFactory<PlayerContext>>();
            services.RemoveAll<PlayerContext>();
            services.RemoveAll<DbContextOptions<PlayerContext>>();

            services.AddDbContext<PlayerContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Replace authentication with a test scheme
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            // Ensure the database is created and migrated
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlayerContext>();
            db.Database.EnsureCreated();
        });
    }
}
