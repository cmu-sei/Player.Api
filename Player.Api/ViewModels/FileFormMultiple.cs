// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Player.Api.ViewModels
{
    public class FileForm
    {
        public Guid viewId { get; set; }
        public List<Guid> teamIds { get; set; }
        public List<IFormFile> ToUpload { get; set; }
    }
}