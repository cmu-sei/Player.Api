// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class PermissionEntity : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Immutable { get; set; }
    }

    public class PermissionConfiguration : IEntityTypeConfiguration<PermissionEntity>
    {
        public void Configure(EntityTypeBuilder<PermissionEntity> builder)
        {
            builder.HasIndex(x => x.Name).IsUnique();
        }
    }

    public enum SystemPermission
    {
        CreateViews,
        ViewViews,
        EditViews,
        ManageViews,
        ViewUsers,
        ManageUsers,
        ViewApplications,
        ManageApplications,
        ViewRoles,
        ManageRoles,
        ViewWebhookSubscriptions,
        ManageWebhookSubscriptions
    }
}
