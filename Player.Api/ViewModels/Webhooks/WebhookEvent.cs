/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using Player.Api.Data.Data.Models.Webhooks;

namespace Player.Api.ViewModels.Webhooks
{
    public class WebhookEvent
    {
        public WebhookEvent(EventType eventType, IWebhookEventPayload payload)
        {
            this.Type = eventType;
            this.Timestamp = DateTime.UtcNow;
            this.Payload = payload;
        }

        public EventType Type { get; }
        public DateTime Timestamp { get; }
        public IWebhookEventPayload Payload { get; }
    }
}