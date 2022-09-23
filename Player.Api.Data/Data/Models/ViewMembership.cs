// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json.Serialization;

namespace Player.Api.Data.Data.Models
{
    public class ViewMembershipEntity
    {
        public ViewMembershipEntity() { }

        public ViewMembershipEntity(Guid viewId, Guid userId)
        {
            ViewId = viewId;
            UserId = userId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public Guid ViewId { get; set; }
        public virtual ViewEntity View { get; set; }

        public Guid UserId { get; set; }
        public virtual UserEntity User { get; set; }

        public Guid? PrimaryTeamMembershipId { get; set; }
        public virtual TeamMembershipEntity PrimaryTeamMembership { get; set; }

        public virtual ICollection<TeamMembershipEntity> TeamMemberships { get; set; } = new List<TeamMembershipEntity>();
    }

    public class ViewMembershipConfiguration : IEntityTypeConfiguration<ViewMembershipEntity>
    {
        public void Configure(EntityTypeBuilder<ViewMembershipEntity> builder)
        {
            builder.HasIndex(e => new { e.ViewId, e.UserId }).IsUnique();

            builder
                .HasOne(em => em.View)
                .WithMany(e => e.Memberships)
                .HasForeignKey(em => em.ViewId);

            builder
                .HasOne(em => em.User)
                .WithMany(u => u.ViewMemberships)
                .HasForeignKey(em => em.UserId)
                .HasPrincipalKey(u => u.Id);

            builder
                .HasOne(x => x.PrimaryTeamMembership);


            //.WithOne(y => y.ViewMembership)
            //.HasForeignKey<ViewMembershipEntity>(x => x.PrimaryTeamMembershipId);
        }
    }
}
