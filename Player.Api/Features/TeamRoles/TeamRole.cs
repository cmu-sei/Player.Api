// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Player.Api.Features.TeamPermissions;

namespace Player.Api.Features.TeamRoles
{
    public class TeamRole
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public bool AllPermissions { get; set; }
        public bool Immutable { get; set; }

        public List<TeamPermissionModel> Permissions { get; set; }
    }

    public record SeedTeamRole : Create.Command
    {
        [ConfigurationKeyName("Permissions")]
        public string[] PermissionNames { get; set; }
    }
}
