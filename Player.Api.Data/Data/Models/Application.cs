// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Player.Api.Data.Data.Models
{
    public class ApplicationTemplateEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Url { get; set; }

        public string Icon { get; set; }

        public bool Embeddable { get; set; }
        public bool LoadInBackground { get; set; }

        public ApplicationTemplateEntity()
        {

        }
    }

    public class ApplicationEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Url { get; set; }

        public string Icon { get; set; }

        public bool? Embeddable { get; set; }
        public bool? LoadInBackground { get; set; }

        public Guid ViewId { get; set; }
        public virtual ViewEntity View { get; set; }

        [ForeignKey(nameof(Template))]
        public Guid? ApplicationTemplateId { get; set; }
        public virtual ApplicationTemplateEntity Template { get; set; }

        public ApplicationEntity()
        {

        }

        public ApplicationEntity Clone()
        {
            var entity = this.MemberwiseClone() as ApplicationEntity;
            entity.Id = Guid.Empty;
            entity.ViewId = Guid.Empty;
            entity.View = null;

            return entity;
        }

        public string GetName()
        {
            string name = null;

            if (this.Name != null)
            {
                name = this.Name;
            }
            else if (this.Template != null)
            {
                name = this.Template.Name;
            }

            return name;
        }
    }

    public class ApplicationInstanceEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid TeamId { get; set; }
        public virtual TeamEntity Team { get; set; }

        public Guid ApplicationId { get; set; }
        public virtual ApplicationEntity Application { get; set; }

        public float DisplayOrder { get; set; }

        public ApplicationInstanceEntity() { }

        public ApplicationInstanceEntity Clone()
        {
            var entity = this.MemberwiseClone() as ApplicationInstanceEntity;
            entity.Id = Guid.Empty;
            entity.TeamId = Guid.Empty;
            entity.Team = null;
            entity.Application = null;
            entity.ApplicationId = Guid.Empty;

            return entity;
        }
    }
}
