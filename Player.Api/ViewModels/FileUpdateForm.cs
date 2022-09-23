// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Player.Api.ViewModels
{
    public class FileUpdateForm
    {
        public string Name { get; set; }
        public List<Guid> TeamIds { get; set; }
        public IFormFile ToUpload { get; set; }
    }
}