// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Player.Api.Services
{
    public class ConnectionCacheService
    {
        public readonly ConcurrentDictionary<Guid, List<string>> ViewMembershipConnections = new ConcurrentDictionary<Guid, List<string>>();
        public readonly ConcurrentDictionary<Guid, object> Locks = new ConcurrentDictionary<Guid, object>();

        public ConnectionCacheService() { }
    }
}
