// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Services;

namespace Player.Api.Features.Views;

public enum ImportViewFailureType
{
    ViewExists,
    Other
}

public record ImportViewFailure(Guid? Id, string Name, string Reason, ImportViewFailureType FailureType = ImportViewFailureType.Other);

public class ViewImporter(IMapper mapper, PlayerContext db, IFileService fileService)
{
    public async Task<IEnumerable<ImportViewFailure>> Import(ViewExport[] views, Dictionary<string, byte[]> fileData, bool matchApplicationTemplatesByName, bool matchRolesByName, CancellationToken cancellationToken)
    {
        var failures = new List<ImportViewFailure>();
        var dbViews = await db.Views
                .Where(v => views.Select(x => x.Id).Contains(v.Id))
                .ToListAsync(cancellationToken);

        var dbApplicationTemplates = await db.ApplicationTemplates.ToListAsync(cancellationToken);
        var dbTeamRoles = await db.TeamRoles.ToListAsync(cancellationToken);

        foreach (var view in views)
        {
            if (dbViews.Any(v => v.Id == view.Id))
            {
                failures.Add(new(view.Id, view.Name, "A View with this Id already exists", ImportViewFailureType.ViewExists));
                continue;
            }

            var viewFailures = new List<ImportViewFailure>();
            viewFailures.AddRange(ValidateApplications(view, dbApplicationTemplates, matchApplicationTemplatesByName));
            viewFailures.AddRange(ValidateTeamRoles(view, dbTeamRoles, matchRolesByName));

            var viewEntity = mapper.Map<ViewEntity>(view);

            viewFailures.AddRange(await ValidateFiles(viewEntity, fileData, fileService, cancellationToken));

            if (viewFailures.Count == 0)
            {
                db.Views.Add(viewEntity);
            }
            else
            {
                failures.AddRange(viewFailures);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return failures;
    }

    private static IEnumerable<ImportViewFailure> ValidateApplications(
        ViewExport view,
        IEnumerable<ApplicationTemplateEntity> dbTemplates,
        bool matchByName)
    {
        foreach (var app in view.Applications.Where(x => x.ApplicationTemplateId.HasValue || !string.IsNullOrEmpty(x.ApplicationTemplateName)))
        {
            var dbApp = dbTemplates.FirstOrDefault(x => x.Id == app.ApplicationTemplateId) ??
                (matchByName ? dbTemplates.FirstOrDefault(x => x.Name == app.ApplicationTemplateName) : null);

            if (dbApp is null)
            {
                yield return new ImportViewFailure(
                    view.Id,
                    view.Name,
                    $"Application {app.Name} ({app.Id}): No matching Application Template found");
            }

            app.ApplicationTemplateId = dbApp.Id;
        }
    }

    private static IEnumerable<ImportViewFailure> ValidateTeamRoles(
        ViewExport view,
        IEnumerable<TeamRoleEntity> dbTeamRoles,
        bool matchByName)
    {
        foreach (var team in view.Teams.Where(x => x.RoleId.HasValue || !string.IsNullOrEmpty(x.RoleName)))
        {
            var dbRole = dbTeamRoles.FirstOrDefault(x => x.Id == team.RoleId) ??
                (matchByName ? dbTeamRoles.FirstOrDefault(x => x.Name == team.RoleName) : null);

            if (dbRole is null)
            {
                yield return new ImportViewFailure(
                    view.Id,
                    view.Name,
                    $"Team {team.Name} ({team.Id}): No matching Role found");
            }

            team.RoleId = dbRole.Id;
        }
    }

    private static async Task<IEnumerable<ImportViewFailure>> ValidateFiles(
        ViewEntity viewEntity,
        Dictionary<string, byte[]> fileData,
        IFileService fileService,
        CancellationToken ct)
    {
        var failures = new List<ImportViewFailure>();

        foreach (var file in viewEntity.Files)
        {
            if (!fileData.TryGetValue($"{file.Id}-{file.Name}", out var fileBytes))
            {
                failures.Add(new(
                    viewEntity.Id,
                    viewEntity.Name,
                    $"File {file.Name} ({file.Id}): No matching file found in archive"));
                continue;
            }

            try
            {
                file.Path = await fileService.SaveFile(file, fileBytes, ct);
            }
            catch (Exception ex)
            {
                failures.Add(new(
                    viewEntity.Id,
                    viewEntity.Name,
                    $"File {file.Name} ({file.Id}): {ex.Message}"));
            }
        }

        return failures;
    }
}