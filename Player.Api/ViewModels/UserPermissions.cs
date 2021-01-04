// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json.Serialization;
using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Player.Api.ViewModels
{
    public class UserPermissions
    {
        public IEnumerable<PermissionEntity> Permissions
        {
            get
            {
                return RolePermissions.Concat(AssignedPermissions).Distinct();
            }
        }
        public IEnumerable<TeamPermissions> TeamPermissions { get; set; }

        public IEnumerable<PermissionEntity> RolePermissions { get; set; }

        public IEnumerable<PermissionEntity> AssignedPermissions { get; set; }
    }

    public class TeamPermissions
    {
        public Guid TeamId { get; set; }
        public Guid ViewId { get; set; }
        public bool IsPrimary { get; set; }
        public IEnumerable<PermissionEntity> Permissions
        {
            get
            {
                return RolePermissions
                    .Concat(TeamRolePermissions)
                    .Concat(TeamAssignedPermissions)
                    .Distinct();
            }
        }

        public IEnumerable<PermissionEntity> RolePermissions { get; set; }
        public IEnumerable<PermissionEntity> TeamRolePermissions { get; set; }
        public IEnumerable<PermissionEntity> TeamAssignedPermissions { get; set; }
    }
}
