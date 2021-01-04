// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Player.Api.ViewModels
{
    public class Role
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public List<Permission> Permissions { get; set; }
    }
}
