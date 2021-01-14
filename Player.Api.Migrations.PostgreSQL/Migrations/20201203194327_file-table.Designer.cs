﻿// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Player.Api.Data.Data;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    [DbContext(typeof(PlayerContext))]
    [Migration("20201203194327_file-table")]
    partial class filetable
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .HasAnnotation("ProductVersion", "3.1.9")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("ApplicationTemplateId")
                        .HasColumnName("application_template_id")
                        .HasColumnType("uuid");

                    b.Property<bool?>("Embeddable")
                        .HasColumnName("embeddable")
                        .HasColumnType("boolean");

                    b.Property<string>("Icon")
                        .HasColumnName("icon")
                        .HasColumnType("text");

                    b.Property<bool?>("LoadInBackground")
                        .HasColumnName("load_in_background")
                        .HasColumnType("boolean");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<string>("Url")
                        .HasColumnName("url")
                        .HasColumnType("text");

                    b.Property<Guid>("ViewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("ApplicationTemplateId");

                    b.HasIndex("ViewId");

                    b.ToTable("applications");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationInstanceEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("ApplicationId")
                        .HasColumnName("application_id")
                        .HasColumnType("uuid");

                    b.Property<float>("DisplayOrder")
                        .HasColumnName("display_order")
                        .HasColumnType("real");

                    b.Property<Guid>("TeamId")
                        .HasColumnName("team_id")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("ApplicationId");

                    b.HasIndex("TeamId");

                    b.ToTable("application_instances");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationTemplateEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<bool>("Embeddable")
                        .HasColumnName("embeddable")
                        .HasColumnType("boolean");

                    b.Property<string>("Icon")
                        .HasColumnName("icon")
                        .HasColumnType("text");

                    b.Property<bool>("LoadInBackground")
                        .HasColumnName("load_in_background")
                        .HasColumnType("boolean");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<string>("Url")
                        .HasColumnName("url")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("application_templates");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.FileEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<string>("path")
                        .HasColumnName("path")
                        .HasColumnType("text");

                    b.Property<Guid>("viewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.ToTable("files");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.NotificationEntity", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("key")
                        .HasColumnType("integer")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<DateTime>("BroadcastTime")
                        .HasColumnName("broadcast_time")
                        .HasColumnType("timestamp without time zone");

                    b.Property<Guid>("FromId")
                        .HasColumnName("from_id")
                        .HasColumnType("uuid");

                    b.Property<string>("FromName")
                        .HasColumnName("from_name")
                        .HasColumnType("text");

                    b.Property<int>("FromType")
                        .HasColumnName("from_type")
                        .HasColumnType("integer");

                    b.Property<string>("Link")
                        .HasColumnName("link")
                        .HasColumnType("text");

                    b.Property<int>("Priority")
                        .HasColumnName("priority")
                        .HasColumnType("integer");

                    b.Property<string>("Subject")
                        .HasColumnName("subject")
                        .HasColumnType("text");

                    b.Property<string>("Text")
                        .HasColumnName("text")
                        .HasColumnType("text");

                    b.Property<Guid>("ToId")
                        .HasColumnName("to_id")
                        .HasColumnType("uuid");

                    b.Property<string>("ToName")
                        .HasColumnName("to_name")
                        .HasColumnType("text");

                    b.Property<int>("ToType")
                        .HasColumnName("to_type")
                        .HasColumnType("integer");

                    b.Property<Guid?>("ViewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

                    b.HasKey("Key");

                    b.ToTable("notifications");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.PermissionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Description")
                        .HasColumnName("description")
                        .HasColumnType("text");

                    b.Property<string>("Key")
                        .HasColumnName("key")
                        .HasColumnType("text");

                    b.Property<bool>("ReadOnly")
                        .HasColumnName("read_only")
                        .HasColumnType("boolean");

                    b.Property<string>("Value")
                        .HasColumnName("value")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("Key", "Value")
                        .IsUnique();

                    b.ToTable("permissions");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.RoleEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("roles");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.RolePermissionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnName("permission_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("RoleId")
                        .HasColumnName("role_id")
                        .HasColumnType("uuid");

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
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<Guid?>("RoleId")
                        .HasColumnName("role_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("ViewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("ViewId");

                    b.ToTable("teams");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamMembershipEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("RoleId")
                        .HasColumnName("role_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("TeamId")
                        .HasColumnName("team_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("UserId")
                        .HasColumnName("user_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("ViewMembershipId")
                        .HasColumnName("view_membership_id")
                        .HasColumnType("uuid");

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
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnName("permission_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("TeamId")
                        .HasColumnName("team_id")
                        .HasColumnType("uuid");

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
                        .HasColumnName("key")
                        .HasColumnType("integer")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<Guid>("Id")
                        .HasColumnName("id")
                        .HasColumnType("uuid");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<Guid?>("RoleId")
                        .HasColumnName("role_id")
                        .HasColumnType("uuid");

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
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnName("permission_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("UserId")
                        .HasColumnName("user_id")
                        .HasColumnType("uuid");

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
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Description")
                        .HasColumnName("description")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<int>("Status")
                        .HasColumnName("status")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.ToTable("views");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ViewMembershipEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("PrimaryTeamMembershipId")
                        .HasColumnName("primary_team_membership_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("UserId")
                        .HasColumnName("user_id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("ViewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

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
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.RoleEntity", "Role")
                        .WithMany()
                        .HasForeignKey("RoleId");
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
                });
#pragma warning restore 612, 618
        }
    }
}
