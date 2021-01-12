// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Player.Api.Data.Data.Models
{
    public class TeamEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public Guid? RoleId { get; set; }
        public RoleEntity Role { get; set; }

        public Guid ViewId { get; set; }
        public virtual ViewEntity View { get; set; }

        public virtual ICollection<ApplicationInstanceEntity> Applications { get; set; } = new List<ApplicationInstanceEntity>();
        public virtual ICollection<TeamMembershipEntity> Memberships { get; set; } = new List<TeamMembershipEntity>();
        public virtual ICollection<TeamPermissionEntity> Permissions { get; set; } = new List<TeamPermissionEntity>();

        public TeamEntity() { }

        public TeamEntity Clone()
        {
            var entity = this.MemberwiseClone() as TeamEntity;
            entity.Applications = new List<ApplicationInstanceEntity>();
            entity.Memberships = new List<TeamMembershipEntity>();
            entity.Permissions = new List<TeamPermissionEntity>();
            entity.Id = Guid.Empty;
            entity.ViewId = Guid.Empty;
            entity.View = null;

            return entity;
        }
    }
}
