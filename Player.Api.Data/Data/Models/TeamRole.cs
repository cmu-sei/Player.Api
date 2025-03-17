// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Player.Api.Data.Data.Models
{
    public class TeamRoleEntity : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public bool AllPermissions { get; set; }
        public bool Immutable { get; set; }

        public virtual ICollection<TeamRolePermissionEntity> Permissions { get; set; } = new List<TeamRolePermissionEntity>();
    }

    public class TeamRoleConfiguration : IEntityTypeConfiguration<TeamRoleEntity>
    {
        public void Configure(EntityTypeBuilder<TeamRoleEntity> builder)
        {
            builder.HasIndex(e => new { e.Name }).IsUnique();
        }
    }
}
