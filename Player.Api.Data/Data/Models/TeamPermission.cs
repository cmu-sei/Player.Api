// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class TeamPermissionEntity
    {
        public TeamPermissionEntity() { }

        public TeamPermissionEntity(Guid teamId, Guid permissionId)
        {
            TeamId = teamId;
            PermissionId = permissionId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid TeamId { get; set; }
        public virtual TeamEntity Team { get; set; }

        public Guid PermissionId { get; set; }
        public virtual PermissionEntity Permission { get; set; }
    }

    public class TeamPermissionConfiguration : IEntityTypeConfiguration<TeamPermissionEntity>
    {
        public void Configure(EntityTypeBuilder<TeamPermissionEntity> builder)
        {
            builder.HasIndex(x => new { x.TeamId, x.PermissionId }).IsUnique();

            builder
                .HasOne(rp => rp.Team)
                .WithMany(r => r.Permissions);
        }
    }
}
