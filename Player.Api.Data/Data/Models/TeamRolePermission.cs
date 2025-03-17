// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class TeamRolePermissionEntity : IEntity
    {
        public TeamRolePermissionEntity() { }

        public TeamRolePermissionEntity(Guid roleId, Guid permissionId)
        {
            RoleId = roleId;
            PermissionId = permissionId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid RoleId { get; set; }
        public virtual TeamRoleEntity Role { get; set; }

        public Guid PermissionId { get; set; }
        public virtual TeamPermissionEntity Permission { get; set; }
    }

    public class TeamRolePermissionConfiguration : IEntityTypeConfiguration<TeamRolePermissionEntity>
    {
        public void Configure(EntityTypeBuilder<TeamRolePermissionEntity> builder)
        {
            builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();

            builder
                .HasOne(x => x.Role)
                .WithMany(x => x.Permissions);
        }
    }
}
