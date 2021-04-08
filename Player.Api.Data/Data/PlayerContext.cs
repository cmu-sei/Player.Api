// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Data.Data.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Player.Api.Data.Data
{
    public class PlayerContext : DbContext
    {
        private DbContextOptions<PlayerContext> _options;

        public PlayerContext(DbContextOptions<PlayerContext> options) : base(options)
        {
            _options = options;
        }

        public DbSet<UserEntity> Users { get; set; }
        public DbSet<ViewEntity> Views { get; set; }
        public DbSet<TeamEntity> Teams { get; set; }
        public DbSet<ApplicationTemplateEntity> ApplicationTemplates { get; set; }
        public DbSet<ApplicationEntity> Applications { get; set; }
        public DbSet<ApplicationInstanceEntity> ApplicationInstances { get; set; }
        public DbSet<NotificationEntity> Notifications { get; set; }
        public DbSet<ViewMembershipEntity> ViewMemberships { get; set; }
        public DbSet<TeamMembershipEntity> TeamMemberships { get; set; }
        public DbSet<RoleEntity> Roles { get; set; }
        public DbSet<PermissionEntity> Permissions { get; set; }
        public DbSet<RolePermissionEntity> RolePermissions { get; set; }
        public DbSet<TeamPermissionEntity> TeamPermissions { get; set; }
        public DbSet<UserPermissionEntity> UserPermissions { get; set; }
        public DbSet<FileEntity> Files { get; set; }
        public DbSet<WebhookSubscriptionEntity> Webhooks { get; set; }
        public DbSet<WebhookSubscriptionEventTypeEntity> WebhookSubscriptionEventTypes { get; set; }
        public DbSet<PendingEventEntity> PendingEvents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurations();

            // Apply PostgreSQL specific options
            if (Database.IsNpgsql())
            {
                modelBuilder.AddPostgresUUIDGeneration();
                modelBuilder.UsePostgresCasing();
            }
        }
    }
}
