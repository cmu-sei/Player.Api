// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Player.Api.Options
{
    public class FileUploadOptions
    {
        public string basePath { get; set; }
        public long maxSize { get; set; }
        public string[] allowedExtensions { get; set; }
    }
}