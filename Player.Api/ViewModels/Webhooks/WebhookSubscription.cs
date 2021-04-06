using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Player.Api.ViewModels.Webhooks
{
    public class WebhookSubscription
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string CallbackUri { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public List<Player.Api.Data.Data.Models.Webhooks.EventType> EventTypes { get; set; }
    }
}