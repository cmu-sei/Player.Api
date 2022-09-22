// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;

namespace Player.Api.ViewModels
{
    public class TeamDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid ViewId { get; set; }
        public Guid? RoleId { get; set; }
        public string RoleName { get; set; }
        public List<Permission> Permissions { get; set; }
    }
}
