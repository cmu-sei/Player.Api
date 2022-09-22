// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Player.Api.ViewModels
{
    public class FileModel
    {
        public Guid id { get; set; }
        public string Name { get; set; }
        public Guid viewId { get; set; }
        public List<Guid> teamIds { get; set; }
        public string Path { get; set; }
    }
}