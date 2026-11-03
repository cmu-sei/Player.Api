// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Infrastructure.Mappings;

/// <summary>File mappings, used by <c>FileService</c>.</summary>
/// <remarks>
/// The file view models name their members in camel case (<c>id</c>, <c>viewId</c>, <c>teamIds</c>), so
/// members match case-insensitively, as they did through AutoMapper.
/// </remarks>
[Mapper(PropertyNameMappingStrategy = PropertyNameMappingStrategy.CaseInsensitive)]
public static partial class FileMapper
{
    [MapProperty(nameof(@FileEntity.View.Id), nameof(FileModel.viewId))]
    [MapProperty(nameof(FileEntity.TeamIds), nameof(FileModel.teamIds), Use = nameof(CopyTeamIds))]
    public static partial FileModel ToFileModel(this FileEntity source);

    public static List<FileModel> ToFileModels(this IEnumerable<FileEntity> source) =>
        source.Select(x => x.ToFileModel()).ToList();

    /// <summary>
    /// A new entity for one uploaded file. <c>FileService</c> sets the name, path and id, and attaches the
    /// view.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(FileForm.viewId))]
    [MapperIgnoreSource(nameof(FileForm.ToUpload))]
    [MapProperty(nameof(FileForm.teamIds), nameof(FileEntity.TeamIds), Use = nameof(CopyTeamIds))]
    public static partial FileEntity ToEntity(this FileForm source);

    /// <summary>
    /// Each entity and view model gets its own list, and a missing list becomes an empty one, as
    /// AutoMapper's collection mapping did. Uploading several files from one form would otherwise
    /// share the form's list, and <c>FileService.GetByTeamAsync</c> cannot read a null one.
    /// </summary>
    private static List<Guid> CopyTeamIds(List<Guid> teamIds) => teamIds?.ToList() ?? [];
}
