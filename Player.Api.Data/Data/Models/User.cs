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
    public class UserEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Key { get; set; }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public Guid? RoleId { get; set; }
        public virtual RoleEntity Role { get; set; }

        public ICollection<ViewMembershipEntity> ViewMemberships { get; set; } = new List<ViewMembershipEntity>();
        public ICollection<TeamMembershipEntity> TeamMemberships { get; set; } = new List<TeamMembershipEntity>();
        public ICollection<UserPermissionEntity> Permissions { get; set; } = new List<UserPermissionEntity>();
    }

    public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.HasIndex(e => e.Id).IsUnique();
        }
    }
}
