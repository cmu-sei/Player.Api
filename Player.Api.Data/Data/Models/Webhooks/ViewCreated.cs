using System;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class ViewCreated : WebhookEvent
    {
        public Guid ViewId;
        public Guid ParentId;
    }
}