/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Player.Api.ViewModels.Webhooks
{
    public class WebhookSubscriptionForm
    {
        public string Name { get; set; }
        public string CallbackUri { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public List<Player.Api.Data.Data.Models.Webhooks.EventType> EventTypes { get; set; }
    }
}