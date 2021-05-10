/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

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