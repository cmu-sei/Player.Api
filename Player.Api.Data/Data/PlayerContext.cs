// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Data.Data.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Player.Api.Data.Data
{
    public class PlayerContext : DbContext
    {
        // Needed for EventInterceptor
        public IServiceProvider ServiceProvider;

        public PlayerContext(DbContextOptions<PlayerContext> options) : base(options) { }

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
        public DbSet<TeamRoleEntity> TeamRoles { get; set; }
        public DbSet<TeamRolePermissionEntity> TeamRolePermissions { get; set; }
        public DbSet<PermissionEntity> Permissions { get; set; }
        public DbSet<RolePermissionEntity> RolePermissions { get; set; }
        public DbSet<TeamPermissionAssignmentEntity> TeamPermissionAssignments { get; set; }
        public DbSet<TeamPermissionEntity> TeamPermissions { get; set; }
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

            SeedRoles(modelBuilder);
            SeedTeamRoles(modelBuilder);
        }

        private void SeedRoles(ModelBuilder builder)
        {
            Guid administratorRoleId = new("f6c07d62-4f2c-4bd5-82af-bf32c0daccc7");
            Guid contentDeveloperRoleId = new("7fd6aa3e-a765-47b8-a77e-f58eae53a82f");

            builder.Entity<RoleEntity>().HasData(
                new RoleEntity
                {
                    Id = administratorRoleId,
                    Name = "Administrator",
                    AllPermissions = true,
                    Immutable = true
                },
                new RoleEntity
                {
                    Id = contentDeveloperRoleId,
                    Name = "Content Developer",
                    AllPermissions = false,
                    Immutable = false,
                }
            );

            Guid createViewsPermissionId = new("06e2699d-21a9-4053-922a-411499b3e923");

            builder.Entity<PermissionEntity>().HasData(
                new PermissionEntity
                {
                    Id = createViewsPermissionId,
                    Name = SystemPermission.CreateViews.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("06da87d8-7636-4a50-929a-bbff2fbad548"),
                    Name = SystemPermission.ViewViews.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("6c407f81-ab2e-4b24-911b-78b7f424b965"),
                    Name = SystemPermission.EditViews.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("5597959a-ff30-4b73-9122-f21d17c19382"),
                    Name = SystemPermission.ManageViews.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("70f3e368-7e7a-4166-9698-5c96dbb19ceb"),
                    Name = SystemPermission.ViewUsers.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("5cc1e12e-7440-4bd8-9a54-ccb5bb0f3f1e"),
                    Name = SystemPermission.ManageUsers.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("0ef125ff-c493-476d-a041-0b6af54f4d36"),
                    Name = SystemPermission.ViewApplications.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("8dc72622-565d-4b86-b6d7-1692dc803815"),
                    Name = SystemPermission.ManageApplications.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("cfcc8ac3-6591-41b8-abe1-0456616b3798"),
                    Name = SystemPermission.ViewRoles.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("f1416f76-aa64-4edc-bfa8-6f234da85060"),
                    Name = SystemPermission.ManageRoles.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("e15b0177-5250-4886-b062-4029a9371a99"),
                    Name = SystemPermission.ViewWebhookSubscriptions.ToString(),
                    Description = "",
                    Immutable = true
                },
                new PermissionEntity
                {
                    Id = new("e1772ce2-eacb-478f-bac8-2e77d49c608a"),
                    Name = SystemPermission.ManageWebhookSubscriptions.ToString(),
                    Description = "",
                    Immutable = true
                }
            );

            builder.Entity<RolePermissionEntity>().HasData(
                new RolePermissionEntity
                {
                    Id = new("b5ef76d0-6257-4657-a51a-79d1e3850720"),
                    RoleId = contentDeveloperRoleId,
                    PermissionId = createViewsPermissionId
                }
            );
        }

        private void SeedTeamRoles(ModelBuilder builder)
        {
            var observerRoleId = new Guid("c875dcce-2488-4e73-8585-8375b4730151");
            var viewMemberRoleId = new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22");

            builder.Entity<TeamRoleEntity>().HasData(
                new TeamRoleEntity
                {
                    Id = new("b65ce1b0-f995-45e1-93fc-47a09542cee5"),
                    Name = "View Admin",
                    AllPermissions = true,
                    Immutable = true
                },
                new TeamRoleEntity
                {
                    Id = observerRoleId,
                    Name = "Observer",
                    AllPermissions = false,
                    Immutable = false
                },
                new TeamRoleEntity
                {
                    Id = viewMemberRoleId,
                    Name = "View Member",
                    AllPermissions = false,
                    Immutable = false
                }
            );

            var teamPermissions = new List<TeamPermissionEntity>()
            {
                new TeamPermissionEntity
                {
                    Id = new("f3ef9465-7f7c-43ef-9855-83798ce5bcd5"),
                    Name = TeamPermission.ViewTeam.ToString(),
                    Description = "Allows viewing Team resources",
                    Immutable = true
                },
                new TeamPermissionEntity
                {
                    Id = new("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"),
                    Name = TeamPermission.EditTeam.ToString(),
                    Description = "Allows editing basic Team resources, including making changes within Virtual Machines, if applicable.",
                    Immutable = true
                },
                new TeamPermissionEntity
                {
                    Id = new("83e41563-8b7f-4f43-b9d0-2d8dc12fc0bf"),
                    Name = TeamPermission.ManageTeam.ToString(),
                    Description = "Allows managing all Team resources, including adding and removing Users.",
                    Immutable = true
                },
                new TeamPermissionEntity
                {
                    Id = new("dedb382b-9d9f-43dc-b128-bb5f1ad94a15"),
                    Name = ViewPermission.ManageView.ToString(),
                    Description = "Allows managing all resources for all Teams in the View",
                    Immutable = true
                },
                new TeamPermissionEntity
                {
                    Id = new("7be07cd5-104e-4770-800b-80ac26cda6d5"),
                    Name = ViewPermission.ViewView.ToString(),
                    Description = "Allows viewing all resources in the View",
                    Immutable = true
                },
                new TeamPermissionEntity
                {
                    Id = new("5ae96619-b40b-4fdb-bbef-ad476c21553d"),
                    Name = ViewPermission.EditView.ToString(),
                    Description = "Allows editing all basic resources in the View, including making changes within Virtual Machines, if applicable.",
                    Immutable = true
                },

                // Vm.Api Permissions
                new TeamPermissionEntity
                {
                    Id = new("5da3014c-a6a5-4c3c-a658-e86672801313"),
                    Name = "UploadViewIsos",
                    Description = "Allows uploading ISOs that can be used by any Teams in the View",
                    Immutable = false
                },
                new TeamPermissionEntity
                {
                    Id = new("d7271fd0-e47f-4630-a5ef-744acc4dc004"),
                    Name = "UploadTeamIsos",
                    Description = "Allows uploading ISOs that can be used by members of the Team",
                    Immutable = false
                },
                new TeamPermissionEntity
                {
                    Id = new("3b135496-c7d9-4bef-b60c-fbcfa1af9c1b"),
                    Name = "DownloadVmFiles",
                    Description = "Allows downloading files directly from Vms",
                    Immutable = false
                },
                new TeamPermissionEntity
                {
                    Id = new("6e41449b-a5da-4ac0-9adb-432210a5541c"),
                    Name = "UploadVmFiles",
                    Description = "Allows uploading files directly to Vms",
                    Immutable = false
                }
            };

            builder.Entity<TeamPermissionEntity>().HasData(teamPermissions);

            var observerPermissions = teamPermissions.Where(x => x.Name.StartsWith("View"));
            var viewMemberPermissions = teamPermissions.Where(x => new[] {
                TeamPermission.ViewTeam.ToString(),
                TeamPermission.EditTeam.ToString(),
                "UploadTeamIsos",
                "UploadVmFiles",
            }
            .Contains(x.Name));

            builder.Entity<TeamRolePermissionEntity>().HasData(
                // Observer Permissions
                new TeamRolePermissionEntity
                {
                    Id = new("d0715a73-16c3-4e44-be57-6bbf2e25e7b3"),
                    RoleId = observerRoleId,
                    PermissionId = teamPermissions.SingleOrDefault(x => x.Name == TeamPermission.ViewTeam.ToString()).Id
                },
                new TeamRolePermissionEntity
                {
                    Id = new("5939d9cf-6f4b-4136-907c-0e878da2241b"),
                    RoleId = observerRoleId,
                    PermissionId = teamPermissions.SingleOrDefault(x => x.Name == ViewPermission.ViewView.ToString()).Id
                },

                // View Member Permissions
                new TeamRolePermissionEntity
                {
                    Id = new("d1252d24-c25d-4a80-a91c-4ad23efa9f89"),
                    RoleId = viewMemberRoleId,
                    PermissionId = teamPermissions.SingleOrDefault(x => x.Name == TeamPermission.ViewTeam.ToString()).Id
                },
                new TeamRolePermissionEntity
                {
                    Id = new("f83d8368-1839-44d4-ad8c-dfa7fae56565"),
                    RoleId = viewMemberRoleId,
                    PermissionId = teamPermissions.SingleOrDefault(x => x.Name == TeamPermission.EditTeam.ToString()).Id
                },
                new TeamRolePermissionEntity
                {
                    Id = new("8a2d8db9-cb8f-4952-9f3b-1377140f0c11"),
                    RoleId = viewMemberRoleId,
                    PermissionId = teamPermissions.SingleOrDefault(x => x.Name == "UploadTeamIsos").Id
                },
                new TeamRolePermissionEntity
                {
                    Id = new("aba4f8e0-298e-4e10-b0d4-ec6447baad6b"),
                    RoleId = viewMemberRoleId,
                    PermissionId = teamPermissions.SingleOrDefault(x => x.Name == "UploadVmFiles").Id
                }
            );
        }
    }
}
