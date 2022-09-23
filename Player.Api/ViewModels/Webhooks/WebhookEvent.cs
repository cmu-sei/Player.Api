/*
Copyright 2022 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Text.Json;
using Player.Api.Data.Data.Models.Webhooks;

namespace Player.Api.ViewModels.Webhooks
{
    public class WebhookEvent
    {
        public WebhookEvent(EventType eventType, IWebhookEventPayload payload)
        {
            this.Type = eventType;
            this.Timestamp = DateTime.UtcNow;
            this.Payload = JsonSerializer.Serialize((object)payload);
        }

        public Guid Id { get; } = new Guid();
        public EventType Type { get; }
        public DateTime Timestamp { get; }

        // Serialized object of type stored in Type property
        public string Payload { get; }
    }
}