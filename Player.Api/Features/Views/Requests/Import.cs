// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Services;

namespace Player.Api.Features.Views;

public class Import
{
    [DataContract(Name = "ImportViewsCommand")]
    public record Command : IRequest<ImportViewsResult>
    {
        [DataMember] public IFormFile Archive { get; set; } = default!;
        public bool MatchApplicationTemplatesByName { get; set; }
        public bool MatchRolesByName { get; set; }
    }

    public record ImportViewsResult(ImportViewFailure[] Failures);

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group) =>
        [
            group.MapPost("views/actions/import", TypedHandler)
                 .WithName("importViews")
                 .WithDescription("Imports the provided Views")
                 .WithSummary("Imports Views.")
        ];

        async Task<Ok<ImportViewsResult>> TypedHandler(IMediator mediator, [AsParameters] Command command, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(
        IPlayerAuthorizationService authorizationService,
        IArchiveService archiveService,
        ViewImporter viewImporter)
        : BaseHandler<Command, ImportViewsResult>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            authorizationService.Authorize([SystemPermission.ManageViews], [], [], cancellationToken);

        public override async Task<ImportViewsResult> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var extractedFiles = await ExtractArchive(request.Archive, archiveService, cancellationToken);
            var views = DeserializeViews(extractedFiles);

            if (views is null)
            {
                return new ImportViewsResult([new(null, ViewConstants.ExportFileName, $"Missing or invalid {ViewConstants.ExportFileName} in archive")]);
            }

            var failures = await viewImporter.Import(views, extractedFiles, request.MatchApplicationTemplatesByName, request.MatchRolesByName, cancellationToken);
            return new ImportViewsResult(failures.ToArray());
        }

        private static async Task<Dictionary<string, byte[]>> ExtractArchive(IFormFile archive, IArchiveService archiveService, CancellationToken ct)
        {
            using var memStream = new MemoryStream();
            await archive.CopyToAsync(memStream, ct);
            memStream.Position = 0;
            return archiveService.ExtractArchive(memStream, archive.FileName);
        }

        private static ViewExport[] DeserializeViews(Dictionary<string, byte[]> extractedFiles)
        {
            if (!extractedFiles.TryGetValue(ViewConstants.ExportFileName, out var viewsJson))
                return null;

            return JsonSerializer.Deserialize<ViewExport[]>(viewsJson);
        }
    }
}
