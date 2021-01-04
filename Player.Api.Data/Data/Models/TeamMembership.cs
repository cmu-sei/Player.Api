// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json.Serialization;

namespace Player.Api.Data.Data.Models
{
    public class TeamMembershipEntity
    {
        public TeamMembershipEntity() { }

        public TeamMembershipEntity(Guid teamId, Guid userId)
        {
            TeamId = teamId;
            UserId = userId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid TeamId { get; set; }
        public virtual TeamEntity Team { get; set; }

        public Guid UserId { get; set; }
        public virtual UserEntity User { get; set; }

        public Guid ViewMembershipId { get; set; }
        public virtual ViewMembershipEntity ViewMembership { get; set; }

        public Guid? RoleId { get; set; }
        public RoleEntity Role { get; set; }
    }

    public class TeamMembershipConfiguration : IEntityTypeConfiguration<TeamMembershipEntity>
    {
        public void Configure(EntityTypeBuilder<TeamMembershipEntity> builder)
        {
            builder.HasIndex(e => new { e.TeamId, e.UserId }).IsUnique();

            builder
                .HasOne(tu => tu.Team)
                .WithMany(t => t.Memberships)
                .HasForeignKey(tu => tu.TeamId);

            builder
                .HasOne(tm => tm.ViewMembership)
                .WithMany(t => t.TeamMemberships)
                .HasForeignKey(tm => tm.ViewMembershipId);

            builder
                .HasOne(tu => tu.User)
                .WithMany(u => u.TeamMemberships)
                .HasForeignKey(tu => tu.UserId)
                .HasPrincipalKey(u => u.Id);
        }
    }
}
