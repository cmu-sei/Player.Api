// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Options;
using Player.Api.Services;
using Player.Api.ViewModels;

namespace Player.Api.Features.Views;

public class Export
{
    [DataContract(Name = "ExportViewsCommand")]
    public record Query : IRequest<ArchiveResult>
    {
        public Guid[] Ids { get; set; }

        public ArchiveType ArchiveType { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views/actions/export", TypedHandler)
                    .WithName("exportViews")
                    .WithDescription("Exports the specified Views, or all Views if none specified.")
                    .WithSummary("Exports Views.")
                    .Produces(StatusCodes.Status200OK, typeof(FileResult), "application/octet-stream")
            ];
        }

        async Task<FileStreamHttpResult> TypedHandler(IMediator mediator, HttpContext httpContext, [AsParameters] Query query, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(query, cancellationToken);

            if (result.HasErrors)
            {
                httpContext.Response.Headers.Append("X-Archive-Contains-Errors", "true");
            }

            return TypedResults.File(result.Data, result.Type, result.Name);
        }
    }

    public class Handler(
            IPlayerAuthorizationService authorizationService,
            IMapper mapper,
            PlayerContext db,
            IArchiveService archiveService) : BaseHandler<Query, ArchiveResult>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewViews], [], [], cancellationToken);

        public override async Task<ArchiveResult> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            IQueryable<ViewEntity> query = db.Views;

            if (request.Ids.Any())
            {
                query = query.Where(x => request.Ids.Contains(x.Id));
            }

            var items = await query
                .ProjectTo<ViewExportDTO>(mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            var data = new Dictionary<string, object>();
            var errors = new StringBuilder();

            // Get Files
            var fileTasks = items.
                SelectMany(x => x.Files)
                .Select(async file =>
                {
                    byte[] fileBytes = null;

                    try
                    {
                        fileBytes = await File.ReadAllBytesAsync(file.Path, cancellationToken);
                        return (file, fileBytes, null);
                    }
                    catch (Exception ex)
                    {
                        return (file, fileBytes, ex);
                    }
                })
                .ToList();

            var results = await Task.WhenAll(fileTasks);

            foreach (var result in results)
            {
                if (result.fileBytes != null)
                {
                    data.Add($"{result.file.id}-{result.file.Name}", result.fileBytes);
                }
                else if (result.ex != null)
                {
                    errors.AppendLine($"View: {result.file.viewId}");
                    errors.AppendLine($"File: {result.file.Name} ({result.file.id})");
                    errors.AppendLine($"Error: {result.ex.GetType()}");
                    errors.AppendLine();
                }
            }

            var export = mapper.Map<ViewExport[]>(items);

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions()
            {
                WriteIndented = true,
            });
            data.Add(ViewConstants.ExportFileName, json);

            if (errors.Length > 0)
            {
                data.Add(ViewConstants.ErrorFileName, errors.ToString());
            }

            var archive = await archiveService.ArchiveData("views", request.ArchiveType, data);

            if (errors.Length > 0)
            {
                archive.HasErrors = true;
            }

            return archive;
        }
    }
}