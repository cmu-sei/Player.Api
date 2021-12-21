/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

﻿// <auto-generated />
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Player.Api.Data.Data;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    [DbContext(typeof(PlayerContext))]
    [Migration("20211221155143_net6")]
    partial class net6
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "uuid-ossp");
            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("ApplicationTemplateId")
                        .HasColumnType("uuid")
                        .HasColumnName("application_template_id");

                    b.Property<bool?>("Embeddable")
                        .HasColumnType("boolean")
                        .HasColumnName("embeddable");

                    b.Property<string>("Icon")
                        .HasColumnType("text")
                        .HasColumnName("icon");

                    b.Property<bool?>("LoadInBackground")
                        .HasColumnType("boolean")
                        .HasColumnName("load_in_background");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Url")
                        .HasColumnType("text")
                        .HasColumnName("url");

                    b.Property<Guid>("ViewId")
                        .HasColumnType("uuid")
                        .HasColumnName("view_id");

                    b.HasKey("Id");

                    b.HasIndex("ApplicationTemplateId");

                    b.HasIndex("ViewId");

                    b.ToTable("applications");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationInstanceEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("ApplicationId")
                        .HasColumnType("uuid")
                        .HasColumnName("application_id");

                    b.Property<float>("DisplayOrder")
                        .HasColumnType("real")
                        .HasColumnName("display_order");

                    b.Property<Guid>("TeamId")
                        .HasColumnType("uuid")
                        .HasColumnName("team_id");

                    b.HasKey("Id");

                    b.HasIndex("ApplicationId");

                    b.HasIndex("TeamId");

                    b.ToTable("application_instances");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationTemplateEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<bool>("Embeddable")
                        .HasColumnType("boolean")
                        .HasColumnName("embeddable");

                    b.Property<string>("Icon")
                        .HasColumnType("text")
                        .HasColumnName("icon");

                    b.Property<bool>("LoadInBackground")
                        .HasColumnType("boolean")
                        .HasColumnName("load_in_background");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Url")
                        .HasColumnType("text")
                        .HasColumnName("url");

                    b.HasKey("Id");

                    b.ToTable("application_templates");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.FileEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Path")
                        .HasColumnType("text")
                        .HasColumnName("path");

                    b.Property<List<Guid>>("TeamIds")
                        .HasColumnType("uuid[]")
                        .HasColumnName("team_ids");

                    b.Property<Guid?>("ViewId")
                        .HasColumnType("uuid")
                        .HasColumnName("view_id");

                    b.HasKey("Id");

                    b.HasIndex("ViewId");

                    b.ToTable("files");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.NotificationEntity", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("key");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Key"));

                    b.Property<DateTime>("BroadcastTime")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("broadcast_time");

                    b.Property<Guid>("FromId")
                        .HasColumnType("uuid")
                        .HasColumnName("from_id");

                    b.Property<string>("FromName")
                        .HasColumnType("text")
                        .HasColumnName("from_name");

                    b.Property<int>("FromType")
                        .HasColumnType("integer")
                        .HasColumnName("from_type");

                    b.Property<string>("Link")
                        .HasColumnType("text")
                        .HasColumnName("link");

                    b.Property<int>("Priority")
                        .HasColumnType("integer")
                        .HasColumnName("priority");

                    b.Property<string>("Subject")
                        .HasColumnType("text")
                        .HasColumnName("subject");

                    b.Property<string>("Text")
                        .HasColumnType("text")
                        .HasColumnName("text");

                    b.Property<Guid>("ToId")
                        .HasColumnType("uuid")
                        .HasColumnName("to_id");

                    b.Property<string>("ToName")
                        .HasColumnType("text")
                        .HasColumnName("to_name");

                    b.Property<int>("ToType")
                        .HasColumnType("integer")
                        .HasColumnName("to_type");

                    b.Property<Guid?>("ViewId")
                        .HasColumnType("uuid")
                        .HasColumnName("view_id");

                    b.HasKey("Key");

                    b.ToTable("notifications");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.PermissionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<string>("Key")
                        .HasColumnType("text")
                        .HasColumnName("key");

                    b.Property<bool>("ReadOnly")
                        .HasColumnType("boolean")
                        .HasColumnName("read_only");

                    b.Property<string>("Value")
                        .HasColumnType("text")
                        .HasColumnName("value");

                    b.HasKey("Id");

                    b.HasIndex("Key", "Value")
                        .IsUnique();

                    b.ToTable("permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.RoleEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("roles");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.RolePermissionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnType("uuid")
                        .HasColumnName("permission_id");

                    b.Property<Guid>("RoleId")
                        .HasColumnType("uuid")
                        .HasColumnName("role_id");

                    b.HasKey("Id");

                    b.HasIndex("PermissionId");

                    b.HasIndex("RoleId", "PermissionId")
                        .IsUnique();

                    b.ToTable("role_permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uuid")
                        .HasColumnName("role_id");

                    b.Property<Guid>("ViewId")
                        .HasColumnType("uuid")
                        .HasColumnName("view_id");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("ViewId");

                    b.ToTable("teams");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamMembershipEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uuid")
                        .HasColumnName("role_id");

                    b.Property<Guid>("TeamId")
                        .HasColumnType("uuid")
                        .HasColumnName("team_id");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<Guid>("ViewMembershipId")
                        .HasColumnType("uuid")
                        .HasColumnName("view_membership_id");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("UserId");

                    b.HasIndex("ViewMembershipId");

                    b.HasIndex("TeamId", "UserId")
                        .IsUnique();

                    b.ToTable("team_memberships");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamPermissionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnType("uuid")
                        .HasColumnName("permission_id");

                    b.Property<Guid>("TeamId")
                        .HasColumnType("uuid")
                        .HasColumnName("team_id");

                    b.HasKey("Id");

                    b.HasIndex("PermissionId");

                    b.HasIndex("TeamId", "PermissionId")
                        .IsUnique();

                    b.ToTable("team_permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserEntity", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("key");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Key"));

                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uuid")
                        .HasColumnName("role_id");

                    b.HasKey("Key");

                    b.HasIndex("Id")
                        .IsUnique();

                    b.HasIndex("RoleId");

                    b.ToTable("users");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserPermissionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnType("uuid")
                        .HasColumnName("permission_id");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("PermissionId");

                    b.HasIndex("UserId", "PermissionId")
                        .IsUnique();

                    b.ToTable("user_permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ViewEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<int>("Status")
                        .HasColumnType("integer")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.ToTable("views");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ViewMembershipEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("PrimaryTeamMembershipId")
                        .HasColumnType("uuid")
                        .HasColumnName("primary_team_membership_id");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<Guid>("ViewId")
                        .HasColumnType("uuid")
                        .HasColumnName("view_id");

                    b.HasKey("Id");

                    b.HasIndex("PrimaryTeamMembershipId");

                    b.HasIndex("UserId");

                    b.HasIndex("ViewId", "UserId")
                        .IsUnique();

                    b.ToTable("view_memberships");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.ApplicationTemplateEntity", "Template")
                        .WithMany()
                        .HasForeignKey("ApplicationTemplateId");

                    b.HasOne("Player.Api.Data.Data.Models.ViewEntity", "View")
                        .WithMany("Applications")
                        .HasForeignKey("ViewId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Template");

                    b.Navigation("View");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationInstanceEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.ApplicationEntity", "Application")
                        .WithMany()
                        .HasForeignKey("ApplicationId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.TeamEntity", "Team")
                        .WithMany("Applications")
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Application");

                    b.Navigation("Team");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.FileEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.ViewEntity", "View")
                        .WithMany("Files")
                        .HasForeignKey("ViewId");

                    b.Navigation("View");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.RolePermissionEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.PermissionEntity", "Permission")
                        .WithMany()
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.RoleEntity", "Role")
                        .WithMany("Permissions")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Permission");

                    b.Navigation("Role");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.RoleEntity", "Role")
                        .WithMany()
                        .HasForeignKey("RoleId");

                    b.HasOne("Player.Api.Data.Data.Models.ViewEntity", "View")
                        .WithMany("Teams")
                        .HasForeignKey("ViewId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Role");

                    b.Navigation("View");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamMembershipEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.RoleEntity", "Role")
                        .WithMany()
                        .HasForeignKey("RoleId");

                    b.HasOne("Player.Api.Data.Data.Models.TeamEntity", "Team")
                        .WithMany("Memberships")
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.UserEntity", "User")
                        .WithMany("TeamMemberships")
                        .HasForeignKey("UserId")
                        .HasPrincipalKey("Id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.ViewMembershipEntity", "ViewMembership")
                        .WithMany("TeamMemberships")
                        .HasForeignKey("ViewMembershipId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Role");

                    b.Navigation("Team");

                    b.Navigation("User");

                    b.Navigation("ViewMembership");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamPermissionEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.PermissionEntity", "Permission")
                        .WithMany()
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.TeamEntity", "Team")
                        .WithMany("Permissions")
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Permission");

                    b.Navigation("Team");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.RoleEntity", "Role")
                        .WithMany()
                        .HasForeignKey("RoleId");

                    b.Navigation("Role");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserPermissionEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.PermissionEntity", "Permission")
                        .WithMany()
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.UserEntity", "User")
                        .WithMany("Permissions")
                        .HasForeignKey("UserId")
                        .HasPrincipalKey("Id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Permission");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ViewMembershipEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.TeamMembershipEntity", "PrimaryTeamMembership")
                        .WithMany()
                        .HasForeignKey("PrimaryTeamMembershipId");

                    b.HasOne("Player.Api.Data.Data.Models.UserEntity", "User")
                        .WithMany("ViewMemberships")
                        .HasForeignKey("UserId")
                        .HasPrincipalKey("Id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Player.Api.Data.Data.Models.ViewEntity", "View")
                        .WithMany("Memberships")
                        .HasForeignKey("ViewId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("PrimaryTeamMembership");

                    b.Navigation("User");

                    b.Navigation("View");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.RoleEntity", b =>
                {
                    b.Navigation("Permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamEntity", b =>
                {
                    b.Navigation("Applications");

                    b.Navigation("Memberships");

                    b.Navigation("Permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserEntity", b =>
                {
                    b.Navigation("Permissions");

                    b.Navigation("TeamMemberships");

                    b.Navigation("ViewMemberships");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ViewEntity", b =>
                {
                    b.Navigation("Applications");

                    b.Navigation("Files");

                    b.Navigation("Memberships");

                    b.Navigation("Teams");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ViewMembershipEntity", b =>
                {
                    b.Navigation("TeamMemberships");
                });
#pragma warning restore 612, 618
        }
    }
}
