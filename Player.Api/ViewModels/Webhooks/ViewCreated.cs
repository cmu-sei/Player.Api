using System;

namespace Player.Api.ViewModels.Webhooks
{
    public class ViewCreated : WebhookEvent
    {
        public Guid ViewId;
        public Guid ParentId;
    }
}