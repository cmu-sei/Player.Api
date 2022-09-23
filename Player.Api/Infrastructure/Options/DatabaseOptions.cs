// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Options
{
    public class DatabaseOptions
    {
        public bool AutoMigrate { get; set; }
        public bool DevModeRecreate { get; set; }
        public string Provider { get; set; }

        public string SeedTemplateKey { get; set; }
    }
}
