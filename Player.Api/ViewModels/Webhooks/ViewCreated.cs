using System;

namespace Player.Api.ViewModels.Webhooks
{
    public class ViewCreated
    {
        public Guid ViewId { get; set; }
        public Guid ParentId { get; set; }
    }
}