// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Features.Teams;
using Player.Api.ViewModels;

namespace Player.Api.Features.Views
{
    public class View
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ViewStatus Status { get; set; }
        public Guid? ParentViewId { get; set; }
        public bool IsTemplate { get; set; }
        public Guid? DefaultTeamId { get; set; }
    }

    public class ViewExportDTO : View
    {
        public TeamExport[] Teams { get; set; }
        public ApplicationExport[] Applications { get; set; }
        public FileDTO[] Files { get; set; }
    }

    public class ViewExport : View
    {
        public TeamExport[] Teams { get; set; }
        public ApplicationExport[] Applications { get; set; }
        public FileModel[] Files { get; set; }
    }
}
