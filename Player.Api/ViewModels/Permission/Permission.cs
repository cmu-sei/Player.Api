// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using System;

namespace Player.Api.ViewModels
{
    public class Permission
    {
        public Guid Id { get; set; }

        public string Key { get; set; }

        public string Value { get; set; }

        public string Description { get; set; }

        public bool ReadOnly { get; set; }

        //public string[] Tags { get; set; }
    }
}
