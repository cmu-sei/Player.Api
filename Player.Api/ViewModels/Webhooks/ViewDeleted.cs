/*
Copyright 2022 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;

namespace Player.Api.ViewModels.Webhooks
{
    public class ViewDeleted : IWebhookEventPayload
    {
        public Guid ViewId { get; set; }
    }
}