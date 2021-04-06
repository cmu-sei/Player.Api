using System;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class ViewDeleted : WebhookEvent
    {
        public Guid ViewId { get; set; }
        public string ViewName { get; set; }
    }
}