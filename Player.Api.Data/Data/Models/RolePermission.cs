// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class RolePermissionEntity
    {
        public RolePermissionEntity() { }

        public RolePermissionEntity(Guid roleId, Guid permissionId)
        {
            RoleId = roleId;
            PermissionId = permissionId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid RoleId { get; set; }
        public virtual RoleEntity Role { get; set; }

        public Guid PermissionId { get; set; }
        public virtual PermissionEntity Permission { get; set; }
    }

    public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermissionEntity>
    {
        public void Configure(EntityTypeBuilder<RolePermissionEntity> builder)
        {
            builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();

            builder
                .HasOne(rp => rp.Role)
                .WithMany(r => r.Permissions);
        }
    }
}
