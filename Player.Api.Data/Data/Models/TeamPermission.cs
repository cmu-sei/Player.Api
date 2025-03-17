// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class TeamPermissionEntity : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool Immutable { get; set; }
    }

    public class TeamPermissionConfiguration : IEntityTypeConfiguration<TeamPermissionEntity>
    {
        public void Configure(EntityTypeBuilder<TeamPermissionEntity> builder)
        {
            builder.HasIndex(x => new { x.Name }).IsUnique();
        }
    }

    public enum TeamPermission
    {
        ViewTeam,
        EditTeam,
        ManageTeam,
    }

    public enum ViewPermission
    {
        ViewView,
        EditView,
        ManageView
    }
}
