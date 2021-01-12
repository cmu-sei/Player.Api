// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// <auto-generated />
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using System;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    [DbContext(typeof(PlayerContext))]
    [Migration("20180425150608_notification_severity")]
    partial class notification_severity
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("Npgsql:PostgresExtension:uuid-ossp", "'uuid-ossp', '', ''")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("ProductVersion", "2.0.2-rtm-10011");

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid?>("ApplicationTemplateId")
                        .HasColumnName("application_template_id");

                    b.Property<bool?>("Embeddable")
                        .HasColumnName("embeddable");

                    b.Property<Guid>("ExerciseId")
                        .HasColumnName("exercise_id");

                    b.Property<string>("Icon")
                        .HasColumnName("icon");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<string>("Url")
                        .HasColumnName("url");

                    b.HasKey("Id");

                    b.HasIndex("ApplicationTemplateId");

                    b.HasIndex("ExerciseId");

                    b.ToTable("applications");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationInstanceEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("ApplicationId")
                        .HasColumnName("application_id");

                    b.Property<float>("DisplayOrder")
                        .HasColumnName("display_order");

                    b.Property<Guid>("TeamId")
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
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<bool>("Embeddable")
                        .HasColumnName("embeddable");

                    b.Property<string>("Icon")
                        .HasColumnName("icon");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<string>("Url")
                        .HasColumnName("url");

                    b.HasKey("Id");

                    b.ToTable("application_templates");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ExerciseEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Description")
                        .HasColumnName("description");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<int>("Status")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.ToTable("exercises");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.NotificationEntity", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("key");

                    b.Property<DateTime>("BroadcastTime")
                        .HasColumnName("broadcast_time");

                    b.Property<Guid>("FromId")
                        .HasColumnName("from_id");

                    b.Property<int>("FromType")
                        .HasColumnName("from_type");

                    b.Property<string>("Link")
                        .HasColumnName("link");

                    b.Property<int>("Severity")
                        .HasColumnName("severity");

                    b.Property<string>("Subject")
                        .HasColumnName("subject");

                    b.Property<string>("Text")
                        .HasColumnName("text");

                    b.Property<Guid>("ToId")
                        .HasColumnName("to_id");

                    b.Property<int>("ToType")
                        .HasColumnName("to_type");

                    b.HasKey("Key");

                    b.ToTable("notifications");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("ExerciseId")
                        .HasColumnName("exercise_id");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<int>("Role")
                        .HasColumnName("role");

                    b.HasKey("Id");

                    b.HasIndex("ExerciseId");

                    b.ToTable("teams");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamUserEntity", b =>
                {
                    b.Property<Guid>("TeamId")
                        .HasColumnName("team_id");

                    b.Property<Guid>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("TeamId", "UserId");

                    b.HasIndex("UserId");

                    b.ToTable("team_users");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.UserEntity", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("key");

                    b.Property<Guid>("Id")
                        .HasColumnName("id");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<int>("Role")
                        .HasColumnName("role");

                    b.HasKey("Key");

                    b.HasIndex("Id")
                        .IsUnique();

                    b.ToTable("users");
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.ApplicationTemplateEntity", "Template")
                        .WithMany()
                        .HasForeignKey("ApplicationTemplateId");

                    b.HasOne("Player.Api.Data.Data.Models.ExerciseEntity", "Exercise")
                        .WithMany("Applications")
                        .HasForeignKey("ExerciseId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.ApplicationInstanceEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.ApplicationEntity", "Application")
                        .WithMany()
                        .HasForeignKey("ApplicationId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Player.Api.Data.Data.Models.TeamEntity", "Team")
                        .WithMany("Applications")
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.ExerciseEntity", "Exercise")
                        .WithMany("Teams")
                        .HasForeignKey("ExerciseId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Player.Api.Data.Data.Models.TeamUserEntity", b =>
                {
                    b.HasOne("Player.Api.Data.Data.Models.TeamEntity", "Team")
                        .WithMany("TeamUsers")
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Player.Api.Data.Data.Models.UserEntity", "User")
                        .WithMany("TeamUsers")
                        .HasForeignKey("UserId")
                        .HasPrincipalKey("Id")
                        .OnDelete(DeleteBehavior.Cascade);
                });
#pragma warning restore 612, 618
        }
    }
}
