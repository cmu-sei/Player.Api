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
using Microsoft.AspNetCore.Mvc;

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
                        }

                        ProcessSeedDataOptions(seedDataOptions, ctx, mapper);
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

        private static void ProcessSeedDataOptions(SeedDataOptions options, PlayerContext context, IMapper mapper)
        {
            if (options.Permissions.Any())
            {
                var dbPermissions = context.Permissions.ToHashSet();

                foreach (var permission in options.Permissions)
                {
                    if (!dbPermissions.Any(x => x.Name == permission.Name))
                    {
                        var newPermission = mapper.Map<PermissionEntity>(permission);
                        context.Permissions.Add(newPermission);
                    }
                }

                context.SaveChanges();
            }

            if (options.Roles.Any())
            {
                var dbRoles = context.Roles.ToHashSet();

                foreach (var role in options.Roles)
                {
                    if (!dbRoles.Any(x => x.Name == role.Name))
                    {
                        var newRole = mapper.Map<RoleEntity>(role);
                        newRole.Permissions = [];
                        context.Roles.Add(newRole);
                        context.SaveChanges();

                        foreach (var permissionName in role.PermissionNames)
                        {
                            var permission = context.Permissions.FirstOrDefault(x => x.Name == permissionName);

                            if (permission is not null)
                            {
                                context.RolePermissions.Add(new RolePermissionEntity { PermissionId = permission.Id, RoleId = newRole.Id });
                            }
                        }

                        context.SaveChanges();
                    }
                }

                context.SaveChanges();
            }

            if (options.TeamPermissions.Any())
            {
                var dbTeamPermissions = context.TeamPermissions.ToHashSet();

                foreach (var teamPermission in options.TeamPermissions)
                {
                    if (!dbTeamPermissions.Any(x => x.Name == teamPermission.Name))
                    {
                        var newTeamPermission = mapper.Map<TeamPermissionEntity>(teamPermission);
                        context.TeamPermissions.Add(newTeamPermission);
                    }
                }

                context.SaveChanges();
            }

            if (options.TeamRoles.Any())
            {
                var dbTeamRoles = context.TeamRoles.ToHashSet();

                foreach (var teamRole in options.TeamRoles)
                {
                    if (!dbTeamRoles.Any(x => x.Name == teamRole.Name))
                    {
                        var newTeamRole = mapper.Map<TeamRoleEntity>(teamRole);
                        newTeamRole.Permissions = [];
                        context.TeamRoles.Add(newTeamRole);
                        context.SaveChanges();

                        foreach (var permissionName in teamRole.PermissionNames)
                        {
                            var teamPermission = context.TeamPermissions.FirstOrDefault(x => x.Name == permissionName);

                            if (teamPermission is not null)
                            {
                                context.TeamRolePermissions.Add(new TeamRolePermissionEntity { PermissionId = teamPermission.Id, RoleId = newTeamRole.Id });
                            }
                        }

                        context.SaveChanges();
                    }
                }

                context.SaveChanges();
            }

            if (options.Users.Any())
            {
                var dbUserIds = context.Users.Select(x => x.Id).ToHashSet();

                foreach (var user in options.Users)
                {
                    if (!dbUserIds.Contains(user.Id))
                    {
                        var newUser = mapper.Map<UserEntity>(user);

                        if (!string.IsNullOrEmpty(user.Role))
                        {
                            var role = context.Roles.FirstOrDefault(x => x.Name == user.Role);
                            if (role != null)
                            {
                                newUser.RoleId = role.Id;
                            }
                        }

                        context.Users.Add(newUser);
                    }
                }

                context.SaveChanges();
            }

            if (options.Subscriptions.Any())
            {
                var dbSubscriptions = context.Webhooks.ToHashSet();

                foreach (WebhookSubscription subscription in options.Subscriptions)
                {
                    if (!dbSubscriptions.Any(x => x.Name == subscription.Name))
                    {
                        var newSubscription = mapper.Map<WebhookSubscriptionEntity>(subscription);
                        context.Webhooks.Add(newSubscription);
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
