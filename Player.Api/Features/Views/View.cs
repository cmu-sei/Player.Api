// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Player.Api.Data.Data.Models;

namespace Player.Api.Features.Views
{
    public class View
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ViewStatus Status { get; set; }
        public Guid? ParentViewId { get; set; }
    }
}
