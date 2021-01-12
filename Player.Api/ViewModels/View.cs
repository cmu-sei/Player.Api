// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;

namespace Player.Api.ViewModels
{
    public class View
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public ViewStatus Status { get; set; }

        public bool CanManage { get; set; }
        
        public List<Guid> Clones { get; set; }
    }
}
