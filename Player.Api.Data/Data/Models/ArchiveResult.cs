// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;

namespace Player.Api.Data.Models
{
    public class ArchiveResult : IDisposable
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public Stream Data { get; set; }
        public bool HasErrors { get; set; }

        public void Dispose()
        {
            Data.Close();
        }
    }
}