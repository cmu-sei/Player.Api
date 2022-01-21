// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class WebhookSubscriptionEventTypeEntity
    {
        public WebhookSubscriptionEventTypeEntity() { }

        public WebhookSubscriptionEventTypeEntity(Guid subId, EventType eventType)
        {
            SubscriptionId = subId;
            EventType = eventType;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public EventType EventType { get; set; }

        public Guid SubscriptionId { get; set; }
        public virtual WebhookSubscriptionEntity Subscription { get; set; }
    }

    public class WebhookSubscriptionEventTypeEntityConfiguration : IEntityTypeConfiguration<WebhookSubscriptionEventTypeEntity>
    {
        public void Configure(EntityTypeBuilder<WebhookSubscriptionEventTypeEntity> builder)
        {
            builder.HasIndex(x => new { x.SubscriptionId, x.EventType }).IsUnique();

            builder
                .HasOne(et => et.Subscription)
                .WithMany(s => s.EventTypes)
                .HasForeignKey(x => x.SubscriptionId);
        }
    }
}
