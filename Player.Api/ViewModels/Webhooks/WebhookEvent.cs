using System;

namespace Player.Api.ViewModels.Webhooks
{
    public abstract class WebhookEvent
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
    }
}