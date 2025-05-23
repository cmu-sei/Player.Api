/*
Copyright 2022 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class PendingEventEntity : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public EventType EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid SubscriptionId { get; set; }
        public virtual WebhookSubscriptionEntity Subscription { get; set; }
        public string Payload { get; set; }
    }

    public enum EventType
    {
        ViewCreated,
        ViewDeleted
    }
}