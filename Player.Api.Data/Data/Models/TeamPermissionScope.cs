// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    /// <summary>
    /// Scopes a Team's effective permissions onto another Team in the same View.
    /// A Team (TeamId) has its permission scope applied on a TargetTeam (TargetTeamId),
    /// so members of TeamId gain TeamId's effective Team permissions over TargetTeamId
    /// without being members of TargetTeamId.
    /// </summary>
    public class TeamPermissionScopeEntity : IEntity
    {
        public TeamPermissionScopeEntity() { }

        public TeamPermissionScopeEntity(Guid teamId, Guid targetTeamId)
        {
            TeamId = teamId;
            TargetTeamId = targetTeamId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid TeamId { get; set; }
        public virtual TeamEntity Team { get; set; }

        public Guid TargetTeamId { get; set; }
        public virtual TeamEntity TargetTeam { get; set; }
    }

    public class TeamPermissionScopeConfiguration : IEntityTypeConfiguration<TeamPermissionScopeEntity>
    {
        public void Configure(EntityTypeBuilder<TeamPermissionScopeEntity> builder)
        {
            builder.HasIndex(x => new { x.TeamId, x.TargetTeamId }).IsUnique();

            builder
                .HasOne(x => x.Team)
                .WithMany(t => t.Scopes)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(x => x.TargetTeam)
                .WithMany()
                .HasForeignKey(x => x.TargetTeamId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
