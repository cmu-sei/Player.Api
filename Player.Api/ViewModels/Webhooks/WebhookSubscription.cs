/*
Copyright 2022 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Collections.Generic;

namespace Player.Api.ViewModels.Webhooks
{
    public class WebhookSubscription
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string CallbackUri { get; set; }
        public string ClientId { get; set; }

        private string _clientSecret;
        public string ClientSecret
        {
            get
            {
                if (!string.IsNullOrEmpty(_clientSecret))
                {
                    return string.Empty;
                }
                else
                {
                    return null;
                }
            }

            set
            {
                _clientSecret = value;
            }
        }

        public bool ClientSecretSet
        {
            get
            {
                return !string.IsNullOrEmpty(_clientSecret);
            }
        }

        // So Automapper can map a seeded Client Secret, 
        // but it won't be returned from the API
        public string GetClientSecret()
        {
            return _clientSecret;
        }

        public string LastError { get; set; }
        public List<Player.Api.Data.Data.Models.Webhooks.EventType> EventTypes { get; set; }
    }
}