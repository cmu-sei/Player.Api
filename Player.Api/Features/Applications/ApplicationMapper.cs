// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Applications;

/// <summary>
/// The Applications feature's mappings: application templates, applications, and the instances that
/// place an application on a team.
/// </summary>
[Mapper]
public static partial class ApplicationMapper
{
    // ---- Application templates ------------------------------------------------------------------

    public static partial IQueryable<ApplicationTemplate> ProjectToApplicationTemplate(this IQueryable<ApplicationTemplateEntity> query);

    public static partial ApplicationTemplate ToApplicationTemplate(this ApplicationTemplateEntity source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial ApplicationTemplateEntity ToEntity(this CreateApplicationTemplate.Command source);

    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(EditApplicationTemplate.Command.Id))]
    public static partial void ApplyTo(this EditApplicationTemplate.Command source, ApplicationTemplateEntity target);

    /// <summary>A new template from an import file or seed data, keeping its id.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial ApplicationTemplateEntity ToEntity(this ApplicationTemplate source);

    /// <summary>
    /// Overwrites an existing template from an import file. The import matches templates by id, so the
    /// id written is the one the entity already has.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial void ApplyTo(this ApplicationTemplate source, ApplicationTemplateEntity target);

    // ---- Applications ---------------------------------------------------------------------------

    public static partial IQueryable<Application> ProjectToApplication(this IQueryable<ApplicationEntity> query);

    public static partial Application ToApplication(this ApplicationEntity source);

    public static List<Application> ToApplications(this IEnumerable<ApplicationEntity> source) =>
        source.Select(x => x.ToApplication()).ToList();

    /// <remarks>The view comes from the route, and the handler sets it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Create.Command.ViewId))]
    public static partial ApplicationEntity ToEntity(this Create.Command source);

    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, ApplicationEntity target);

    // ---- Application instances ------------------------------------------------------------------

    /// <summary>
    /// Projects instances with the application's settings resolved: a member the application leaves
    /// unset falls back to its template, and the <c>{viewId}</c>, <c>{viewName}</c>, <c>{teamId}</c> and
    /// <c>{teamName}</c> placeholders in the name and url are filled in.
    /// </summary>
    public static partial IQueryable<ApplicationInstance> ProjectToApplicationInstance(this IQueryable<ApplicationInstanceEntity> query);

    [MapPropertyFromSource(nameof(ApplicationInstance.Name), Use = nameof(ResolveName))]
    [MapPropertyFromSource(nameof(ApplicationInstance.Url), Use = nameof(ResolveUrl))]
    [MapPropertyFromSource(nameof(ApplicationInstance.Icon), Use = nameof(ResolveIcon))]
    [MapPropertyFromSource(nameof(ApplicationInstance.Embeddable), Use = nameof(ResolveEmbeddable))]
    [MapPropertyFromSource(nameof(ApplicationInstance.LoadInBackground), Use = nameof(ResolveLoadInBackground))]
    [MapProperty(nameof(@ApplicationInstanceEntity.Application.ViewId), nameof(ApplicationInstance.ViewId))]
    private static partial ApplicationInstance MapToApplicationInstance(ApplicationInstanceEntity source);

    // Expression-bodied, so Mapperly can inline them into the projection rather than call them. They
    // are the AutoMapper member expressions verbatim, so the SQL is unchanged.
    private static string ResolveName(ApplicationInstanceEntity src) =>
        (src.Application.Name ?? src.Application.Template.Name ?? string.Empty)
            .Replace("{viewId}", src.Application.ViewId.ToString())
            .Replace("{viewName}", src.Application.View.Name ?? string.Empty)
            .Replace("{teamId}", src.TeamId.ToString())
            .Replace("{teamName}", src.Team.Name ?? string.Empty);

    // The url escapes the view and team names; the name does not.
    private static string ResolveUrl(ApplicationInstanceEntity src) =>
        (src.Application.Url ?? src.Application.Template.Url ?? string.Empty)
            .Replace("{viewId}", src.Application.ViewId.ToString())
            .Replace("{viewName}", src.Application.View.Name != null ? Uri.EscapeDataString(src.Application.View.Name) : string.Empty)
            .Replace("{teamId}", src.TeamId.ToString())
            .Replace("{teamName}", src.Team.Name != null ? Uri.EscapeDataString(src.Team.Name) : string.Empty);

    private static string ResolveIcon(ApplicationInstanceEntity src) =>
        src.Application.Icon ?? (src.Application.Template != null ? src.Application.Template.Icon : string.Empty);

    private static bool ResolveEmbeddable(ApplicationInstanceEntity src) =>
        src.Application.Embeddable ?? (src.Application.Template != null ? src.Application.Template.Embeddable : false);

    private static bool ResolveLoadInBackground(ApplicationInstanceEntity src) =>
        src.Application.LoadInBackground ?? (src.Application.Template != null ? src.Application.Template.LoadInBackground : false);

    /// <remarks>The team comes from the route, and the handler sets it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(CreateApplicationInstance.Command.TeamId))]
    public static partial ApplicationInstanceEntity ToEntity(this CreateApplicationInstance.Command source);

    /// <summary>
    /// Applies the edit command to an existing instance, including <see cref="ApplicationInstanceFields.ApplicationId"/>,
    /// which moves it to another application.
    /// </summary>
    /// <remarks>The id is the route's, and names the entity being edited; it is never written to it.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(EditApplicationInstance.Command.Id))]
    public static partial void ApplyTo(this EditApplicationInstance.Command source, ApplicationInstanceEntity target);
}
