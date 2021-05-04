/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class WebhookSubscriptionEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string CallbackUri { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string LastError { get; set; }
        public ICollection<WebhookSubscriptionEventTypeEntity> EventTypes { get; set; } = new List<WebhookSubscriptionEventTypeEntity>();
    }
    public enum EventType
    {
        ViewCreated,
        ViewDeleted
    }
}