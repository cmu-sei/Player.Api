/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using S3.Player.Api.Options;
using S3.Player.Api.Data.Data;
using S3.Player.Api.Data.Data.Models;
using S3.Player.Api.Infrastructure.Authorization;
using System.Collections.Generic;

namespace S3.Player.Api.Extensions
{
    public static class DatabaseExtensions
    {
        public static IWebHost InitializeDatabase(this IWebHost webHost)
        {
            using (var scope = webHost.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                try
                {
                    var databaseOptions = services.GetService<DatabaseOptions>();
                    var ctx = services.GetRequiredService<PlayerContext>();
                    var seedDataOptions = services.GetService<SeedDataOptions>();

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
                            ProcessSeedDataOptions(seedDataOptions, ctx);

                            if (!ctx.Views.Any())
                            {
                                Seed.Run(ctx);
                            }

                            ProcessSystemAdminOptions(seedDataOptions.SystemAdminIds, ctx);
                        }
                        else
                        {
                            ProcessSeedDataOptions(seedDataOptions, ctx);
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

        private static void ProcessSeedDataOptions(SeedDataOptions options, PlayerContext context)
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
        }

        public static IServiceCollection AddDbProvider(
          this IServiceCollection services,
          IConfiguration config
      )
        {
            string dbProvider = DbProvider(config);
            switch (dbProvider)
            {
                case "Sqlite":
                    services.AddEntityFrameworkSqlite();
                    break;

                case "SqlServer":
                    services.AddEntityFrameworkSqlServer();
                    break;

                case "PostgreSQL":
                    services.AddEntityFrameworkNpgsql();
                    break;

            }
            return services;
        }

        private static string DbProvider(IConfiguration config)
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
