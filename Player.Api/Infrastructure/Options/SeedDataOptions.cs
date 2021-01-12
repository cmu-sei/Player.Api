// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Options
{
    public class SeedDataOptions
    {
        public List<PermissionEntity> Permissions { get; set; } = new List<PermissionEntity>();
        public List<Guid> SystemAdminIds { get; set; } = new List<Guid>();
    }
}
