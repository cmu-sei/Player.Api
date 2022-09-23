// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class FileEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public virtual ViewEntity View { get; set; }
        public List<Guid> TeamIds { get; set; }
        public string Path { get; set; }

        public FileEntity Clone()
        {
            var entity = this.MemberwiseClone() as FileEntity;
            // entity.TeamIds = new List<Guid>();
            entity.Id = Guid.NewGuid();
            return entity;
        }
    }
}