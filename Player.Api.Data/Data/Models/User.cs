// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Player.Api.Data.Data.Models
{
    public class UserEntity : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Key { get; set; }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public Guid? RoleId { get; set; }
        public virtual RoleEntity Role { get; set; }

        public ICollection<UserIdentityAttributeEntity> IdentityAttributes { get; set; } = new List<UserIdentityAttributeEntity>();
        public ICollection<ViewMembershipEntity> ViewMemberships { get; set; } = new List<ViewMembershipEntity>();
        public ICollection<TeamMembershipEntity> TeamMemberships { get; set; } = new List<TeamMembershipEntity>();
    }

    public class UserIdentityAttributeEntity : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public virtual UserEntity User { get; set; }

        public string Name { get; set; }
        public string Value { get; set; }
        public int DisplayOrder { get; set; }
    }

    public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.HasIndex(e => e.Id).IsUnique();
        }
    }

    public class UserIdentityAttributeConfiguration : IEntityTypeConfiguration<UserIdentityAttributeEntity>
    {
        public void Configure(EntityTypeBuilder<UserIdentityAttributeEntity> builder)
        {
            builder.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

            builder
                .HasOne(e => e.User)
                .WithMany(e => e.IdentityAttributes)
                .HasForeignKey(e => e.UserId)
                .HasPrincipalKey(e => e.Id);
        }
    }
}
