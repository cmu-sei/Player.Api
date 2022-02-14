/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;

namespace Player.Api.ViewModels.Webhooks
{
    public class ViewCreated : IWebhookEventPayload
    {
        public Guid ViewId { get; set; }
        public Guid? ParentId { get; set; }
    }
}