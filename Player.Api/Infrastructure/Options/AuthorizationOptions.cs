// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Options
{
    public class AuthorizationOptions
    {
        public string Authority { get; set; }
        public string AuthorizationUrl { get; set; }
        public string AuthorizationScope { get; set; }
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string ClientSecret { get; set; }
        public bool RequireHttpsMetadata { get; set; }
        public string TokenUrl { get; set; }
    }
}
