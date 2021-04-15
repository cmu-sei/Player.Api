using System;

namespace Player.Api.ViewModels.Webhooks
{
    public class WebhookEvent
    {
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
        public object Payload { get; set; } // Constrain to certain types?
    }
}