// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Player.Api.Data.Data.Models
{
    public class ViewEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public ViewStatus Status { get; set; }

        public virtual ICollection<TeamEntity> Teams { get; set; } = new List<TeamEntity>();
        public virtual ICollection<ApplicationEntity> Applications { get; set; } = new List<ApplicationEntity>();
        public virtual ICollection<ViewMembershipEntity> Memberships { get; set; } = new List<ViewMembershipEntity>();

        public ViewEntity Clone()
        {
            var entity = this.MemberwiseClone() as ViewEntity;
            entity.Id = Guid.Empty;
            entity.Teams = new List<TeamEntity>();
            entity.Applications = new List<ApplicationEntity>();
            entity.Memberships = new List<ViewMembershipEntity>();

            return entity;
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ViewStatus
    {
        Active = 0,
        Inactive = 1
    }
}
