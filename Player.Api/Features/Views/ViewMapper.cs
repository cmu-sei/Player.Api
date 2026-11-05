// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Features.TeamPermissions;
using Player.Api.Features.Teams;
using Player.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Views;

/// <summary>
/// The Views feature's mappings, including the view export file in both directions.
/// </summary>
/// <remarks>
/// The export file is this feature's contract, so the team, application and file shapes inside it are
/// configured here rather than in their own features' mappers: a nested projection cannot reuse another
/// mapper's configuration when it depends on helper methods (see TeamMapper).
/// </remarks>
[Mapper]
public static partial class ViewMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial View ToView(this ViewEntity source);

    public static View[] ToViews(this IEnumerable<ViewEntity> source) =>
        [.. source.Select(x => x.ToView())];

    // ---- Command to entity ----------------------------------------------------------------------

    /// <summary>
    /// A new view from the create command. The handler sets the id the caller chose, and
    /// <see cref="Create.Command.CreateAdminTeam"/> is an instruction to the handler, not a view member.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Create.Command.Id))]
    [MapperIgnoreSource(nameof(Create.Command.CreateAdminTeam))]
    public static partial ViewEntity ToEntity(this Create.Command source);

    /// <summary>
    /// Applies the edit command to an existing view. A null <see cref="Edit.Command.DefaultTeamId"/>
    /// clears the default team.
    /// </summary>
    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, ViewEntity target);

    // ---- Export: entity to export file ----------------------------------------------------------

    /// <summary>
    /// Projects views with everything the export file carries. File paths are included so the export
    /// can read the bytes; <see cref="ToViewExports"/> drops them from the file itself.
    /// </summary>
    public static partial IQueryable<ViewExportDTO> ProjectToViewExportDto(this IQueryable<ViewEntity> query);

    // The collections go through helpers because Mapperly's own collection mapping guards each
    // navigation with a null check, and EF Core loads a whole collection to evaluate one. The helpers
    // are inlined verbatim, as the AutoMapper projection was.
    [MapProperty(nameof(ViewEntity.Teams), nameof(ViewExportDTO.Teams), Use = nameof(MapTeamExports))]
    [MapProperty(nameof(ViewEntity.Applications), nameof(ViewExportDTO.Applications), Use = nameof(MapApplicationExports))]
    [MapProperty(nameof(ViewEntity.Files), nameof(ViewExportDTO.Files), Use = nameof(MapFileDtos))]
    private static partial ViewExportDTO MapToViewExportDto(ViewEntity source);

    [MapProperty(nameof(@TeamEntity.Role.Name), nameof(TeamExport.RoleName))]
    [MapProperty(nameof(TeamEntity.Permissions), nameof(TeamExport.Permissions), Use = nameof(MapAssignedPermissions))]
    [MapProperty(nameof(TeamEntity.Scopes), nameof(TeamExport.ScopedTeamIds), Use = nameof(MapScopedTeamIds))]
    [MapProperty(nameof(TeamEntity.Applications), nameof(TeamExport.Applications), Use = nameof(MapApplicationInstanceExports))]
    private static partial TeamExport MapToTeamExport(TeamEntity source);

    // Expression-bodied, so Mapperly can inline them into the projection rather than call them.
    private static TeamExport[] MapTeamExports(ICollection<TeamEntity> teams) =>
        teams.Select(x => MapToTeamExport(x)).ToArray();

    private static ApplicationExport[] MapApplicationExports(ICollection<ApplicationEntity> applications) =>
        applications.Select(x => MapToApplicationExport(x)).ToArray();

    private static FileDTO[] MapFileDtos(ICollection<FileEntity> files) =>
        files.Select(x => MapToFileDto(x)).ToArray();

    private static List<ApplicationInstanceExport> MapApplicationInstanceExports(ICollection<ApplicationInstanceEntity> instances) =>
        instances.Select(x => MapToApplicationInstanceExport(x)).ToList();

    private static List<TeamPermissionModel> MapAssignedPermissions(ICollection<TeamPermissionAssignmentEntity> assignments) =>
        assignments.Select(x => MapToTeamPermissionModel(x.Permission)).ToList();

    private static List<Guid> MapScopedTeamIds(ICollection<TeamPermissionScopeEntity> scopes) =>
        scopes.Select(x => x.TargetTeamId).ToList();

    [IncludeMappingConfiguration(nameof(@TeamPermissionMapper.ToTeamPermissionModel))]
    private static partial TeamPermissionModel MapToTeamPermissionModel(TeamPermissionEntity source);

    private static partial ApplicationInstanceExport MapToApplicationInstanceExport(ApplicationInstanceEntity source);

    [MapPropertyFromSource(nameof(ApplicationExport.ApplicationTemplateName), Use = nameof(MapTemplateName))]
    private static partial ApplicationExport MapToApplicationExport(ApplicationEntity source);

    private static string MapTemplateName(ApplicationEntity source) =>
        source.Template != null ? source.Template.Name : null;

    [MapProperty(nameof(FileEntity.Id), nameof(FileDTO.id))]
    [MapPropertyFromSource(nameof(FileDTO.viewId), Use = nameof(MapFileViewId))]
    [MapProperty(nameof(FileEntity.TeamIds), nameof(FileDTO.teamIds))]
    private static partial FileDTO MapToFileDto(FileEntity source);

    // Unguarded, as the AutoMapper projection was: an exported file always belongs to the view.
    private static Guid MapFileViewId(FileEntity source) => source.View.Id;

    /// <summary>The export file's contents: the projected views without the file paths.</summary>
    public static ViewExport[] ToViewExports(this IEnumerable<ViewExportDTO> source) =>
        [.. source.Select(x => MapToViewExport(x))];

    private static partial ViewExport MapToViewExport(ViewExportDTO source);

    // Written by hand: Mapperly maps a FileDTO onto its base type with a cast, which would leave each
    // export file entry a FileDTO still carrying its server path, and keep a null team id list null.
    // A new FileModel matches what AutoMapper wrote.
    private static FileModel MapToFileModel(FileDTO source) =>
        source == null
            ? null
            : new FileModel
            {
                id = source.id,
                Name = source.Name,
                viewId = source.viewId,
                teamIds = CopyTeamIds(source.teamIds)
            };

    // ---- Import: export file to entity ----------------------------------------------------------

    /// <summary>
    /// A new view, with its teams, applications and files, from an export file. <c>ViewImporter</c>
    /// resolves roles, templates and team permissions before mapping, and adds permission scopes after.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapProperty(nameof(ViewExport.Teams), nameof(ViewEntity.Teams), Use = nameof(ToTeamEntities))]
    [MapProperty(nameof(ViewExport.Applications), nameof(ViewEntity.Applications), Use = nameof(ToApplicationEntities))]
    [MapProperty(nameof(ViewExport.Files), nameof(ViewEntity.Files), Use = nameof(ToFileEntities))]
    public static partial ViewEntity ToEntity(this ViewExport source);

    // A missing array maps to none, as it did through AutoMapper. Only a missing Files array gets this
    // far: ViewImporter's validation reads Teams and Applications first, and fails on a missing one.
    private static ICollection<TeamEntity> ToTeamEntities(TeamExport[] teams) =>
        teams?.Select(x => MapToTeamEntity(x)).ToList() ?? [];

    private static ICollection<ApplicationEntity> ToApplicationEntities(ApplicationExport[] applications) =>
        applications?.Select(x => MapToApplicationEntity(x)).ToList() ?? [];

    private static ICollection<FileEntity> ToFileEntities(FileModel[] files) =>
        files?.Select(x => MapToFileEntity(x)).ToList() ?? [];

    /// <remarks>
    /// A null role id leaves the entity's empty one, which fails the import's save, as before. The role
    /// name was already resolved to an id, and the scoped team ids become scopes after mapping.
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(TeamExport.RoleName))]
    [MapperIgnoreSource(nameof(TeamExport.ScopedTeamIds))]
    [MapProperty(nameof(TeamExport.Permissions), nameof(TeamEntity.Permissions), Use = nameof(ToPermissionAssignments))]
    [MapProperty(nameof(TeamExport.Applications), nameof(TeamEntity.Applications), Use = nameof(ToApplicationInstanceEntities))]
    private static partial TeamEntity MapToTeamEntity(TeamExport source);

    // The permission ids were resolved against this system by the importer. The team id is filled in
    // by EF Core from the relationship.
    private static ICollection<TeamPermissionAssignmentEntity> ToPermissionAssignments(List<TeamPermissionModel> permissions) =>
        permissions?.Select(x => x.Id).Distinct().Select(x => new TeamPermissionAssignmentEntity { PermissionId = x }).ToList() ?? [];

    private static ICollection<ApplicationInstanceEntity> ToApplicationInstanceEntities(List<ApplicationInstanceExport> instances) =>
        instances?.Select(x => MapToApplicationInstanceEntity(x)).ToList() ?? [];

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    private static partial ApplicationInstanceEntity MapToApplicationInstanceEntity(ApplicationInstanceExport source);

    /// <remarks>The template name was already resolved to an id.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(ApplicationExport.ApplicationTemplateName))]
    private static partial ApplicationEntity MapToApplicationEntity(ApplicationExport source);

    /// <remarks>
    /// <see cref="FileModel.viewId"/> builds a placeholder <see cref="ViewEntity"/>, as AutoMapper's
    /// unflattening did: <c>FileService.SaveFile</c> picks the upload directory from it, and EF Core
    /// replaces it with the imported view when the file is saved.
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapProperty(nameof(FileModel.id), nameof(FileEntity.Id))]
    [MapProperty(nameof(FileModel.viewId), nameof(@FileEntity.View.Id))]
    [MapProperty(nameof(FileModel.teamIds), nameof(FileEntity.TeamIds), Use = nameof(CopyTeamIds))]
    private static partial FileEntity MapToFileEntity(FileModel source);

    // Not a default mapping: Mapperly would otherwise apply it to every List<Guid>, including the
    // export projection's, where it cannot run in the database.
    [UserMapping(Default = false)]
    private static List<Guid> CopyTeamIds(List<Guid> teamIds) => teamIds?.ToList() ?? [];
}
