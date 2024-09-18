// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Player.Api.Options;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using System.Collections.Generic;
using Player.Api.ViewModels.Webhooks;
using Player.Api.Data.Data.Models.Webhooks;
using AutoMapper;
using Microsoft.Extensions.Hosting;

namespace Player.Api.Extensions
{
    public static class DatabaseExtensions
    {
        public static IHost InitializeDatabase(this IHost webHost)
        {
            using (var scope = webHost.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                try
                {
                    var databaseOptions = services.GetService<DatabaseOptions>();
                    var ctx = services.GetRequiredService<PlayerContext>();
                    var seedDataOptions = services.GetService<SeedDataOptions>();
                    var mapper = services.GetRequiredService<IMapper>();

                    if (ctx != null)
                    {
                        if (databaseOptions.DevModeRecreate)
                            ctx.Database.EnsureDeleted();

                        // Do not run migrations on Sqlite, only devModeRecreate allowed
                        if (!ctx.Database.IsSqlite())
                        {
                            ctx.Database.Migrate();
                        }

                        if (databaseOptions.DevModeRecreate)
                        {
                            ctx.Database.EnsureCreated();
                            ProcessSeedDataOptions(seedDataOptions, ctx, mapper);

                            if (!ctx.Views.Any())
                            {
                                Seed.Run(ctx);
                            }

                            ProcessSystemAdminOptions(seedDataOptions.SystemAdminIds, ctx);
                        }
                        else
                        {
                            ProcessSeedDataOptions(seedDataOptions, ctx, mapper);
                            ProcessSystemAdminOptions(seedDataOptions.SystemAdminIds, ctx);
                        }
                    }

                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "An error occurred while initializing the database.");

                    // exit on database connection error on startup so app can be restarted to try again
                    throw;
                }
            }

            return webHost;
        }

        private static void ProcessSystemAdminOptions(List<Guid> ids, PlayerContext context)
        {
            if (ids.Any())
            {
                var users = context.Users
                    .Include(u => u.Permissions)
                        .ThenInclude(p => p.Permission)
                    .Where(u => ids.Contains(u.Id))
                    .ToList();

                var systemAdminPermission = context.Permissions
                    .Where(p => p.Key == PlayerClaimTypes.SystemAdmin.ToString())
                    .FirstOrDefault();

                if (systemAdminPermission != null)
                {
                    foreach (Guid id in ids)
                    {
                        var user = users.Where(u => u.Id == id).FirstOrDefault();

                        if (user != null && !(user.Permissions.Where(p => p.Permission.Key == PlayerClaimTypes.SystemAdmin.ToString()).Any()))
                        {
                            context.UserPermissions.Add(new UserPermissionEntity(user.Id, systemAdminPermission.Id));
                        }
                    }

                    context.SaveChanges();
                }
            }
        }

        private static void ProcessSeedDataOptions(SeedDataOptions options, PlayerContext context, IMapper mapper)
        {
            if (options.Permissions.Any())
            {
                var dbPermissions = context.Permissions.ToList();

                foreach (PermissionEntity permission in options.Permissions)
                {
                    if (!dbPermissions.Where(x => x.Key == permission.Key && x.Value == permission.Value).Any())
                    {
                        context.Permissions.Add(permission);
                    }
                }

                context.SaveChanges();
            }

            if (options.Subscriptions.Any())
            {
                var dbSubscriptions = context.Webhooks.ToList();

                foreach (WebhookSubscription subscription in options.Subscriptions)
                {
                    if (!dbSubscriptions.Where(x => x.Name == subscription.Name).Any())
                    {
                        var dbSubscription = mapper.Map<WebhookSubscriptionEntity>(subscription);
                        context.Webhooks.Add(dbSubscription);
                    }
                }

                context.SaveChanges();
            }
        }

        public static string DbProvider(IConfiguration config)
        {
            return config.GetValue<string>("Database:Provider", "Sqlite").Trim();
        }

        public static DbContextOptionsBuilder UseConfiguredDatabase(
            this DbContextOptionsBuilder builder,
            IConfiguration config
        )
        {
            string dbProvider = DbProvider(config);
            var migrationsAssembly = String.Format("{0}.Migrations.{1}", typeof(Startup).GetTypeInfo().Assembly.GetName().Name, dbProvider);
            var connectionString = config.GetConnectionString(dbProvider);

            switch (dbProvider)
            {
                case "Sqlite":
                    builder.UseSqlite(connectionString, options => options.MigrationsAssembly(migrationsAssembly));
                    break;

                case "SqlServer":
                    builder.UseSqlServer(connectionString, options => options.MigrationsAssembly(migrationsAssembly));
                    break;

                case "PostgreSQL":
                    builder.UseNpgsql(connectionString, options => options.MigrationsAssembly(migrationsAssembly));
                    break;

            }
            return builder;
        }
    }
}
