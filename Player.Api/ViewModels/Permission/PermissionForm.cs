// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using System;

namespace Player.Api.ViewModels
{
    public class PermissionForm
    {
        public string Key { get; set; }

        public string Value { get; set; }

        public string Description { get; set; }

        //public string[] Tags { get; set; }
    }
}
