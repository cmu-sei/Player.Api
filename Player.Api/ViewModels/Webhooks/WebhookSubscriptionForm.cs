/*
Copyright 2022 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System.Collections.Generic;
using Player.Api.Data.Data.Models.Webhooks;

namespace Player.Api.ViewModels.Webhooks
{
    public interface IWebhookSubscriptionForm { }

    public class WebhookSubscriptionForm : IWebhookSubscriptionForm
    {
        public string Name { get; set; }
        public string CallbackUri { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public List<EventType> EventTypes { get; set; }
    }

    public class WebhookSubscriptionPartialEditForm : WebhookSubscriptionForm, IWebhookSubscriptionForm
    {

    }
}