using System;

namespace Player.Api.ViewModels.Webhooks
{
    public class ViewDeleted : WebhookEvent
    {
        public Guid ViewId { get; set; }
        public string ViewName { get; set; }
    }
}