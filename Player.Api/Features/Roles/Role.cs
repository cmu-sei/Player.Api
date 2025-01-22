// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Player.Api.Features.Permissions;

namespace Player.Api.Features.Roles
{
    public class Role
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool AllPermissions { get; set; }
        public bool Immutable { get; set; }
        public List<Permission> Permissions { get; set; }
    }

    public record SeedRole : Create.Command
    {
        [ConfigurationKeyName("Permissions")]
        public string[] PermissionNames { get; set; }
    }
}
