// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class UserPermissionEntity
    {
        public UserPermissionEntity() { }

        public UserPermissionEntity(Guid userId, Guid permissionId)
        {
            UserId = userId;
            PermissionId = permissionId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public virtual UserEntity User { get; set; }

        public Guid PermissionId { get; set; }
        public virtual PermissionEntity Permission { get; set; }
    }

    public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermissionEntity>
    {
        public void Configure(EntityTypeBuilder<UserPermissionEntity> builder)
        {
            builder.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique();

            builder
                .HasOne(rp => rp.User)
                .WithMany(r => r.Permissions)
                .HasPrincipalKey(x => x.Id);
        }
    }
}
