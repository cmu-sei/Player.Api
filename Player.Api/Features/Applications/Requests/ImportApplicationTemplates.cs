// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Services;

namespace Player.Api.Features.Applications;

public class ImportApplicationTemplates
{
    [DataContract(Name = "ImportApplicationTemplatesQuery")]
    public class Command : IRequest<ImportApplicationTemplatesResult>
    {
        [DataMember]
        public IFormFile Archive { get; set; }

        public bool OverWriteExisting { get; set; }
    }

    public class ImportApplicationTemplatesResult
    {
        public string[] Failures { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("application-templates/actions/import", TypedHandler)
                    .WithName("importApplicationTemplates")
                    .WithDescription("Import Application Templates")
                    .WithSummary("Import Application Templates.")
            ];
        }

        async Task<Ok<ImportApplicationTemplatesResult>> TypedHandler(IMediator mediator, [AsParameters] Command command, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IArchiveService archiveService, IMapper mapper) : BaseHandler<Command, ImportApplicationTemplatesResult>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageApplications], [], [], cancellationToken);

        public override async Task<ImportApplicationTemplatesResult> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            Dictionary<string, byte[]> extractedFiles;
            var failures = new List<string>();

            using (var memStream = new System.IO.MemoryStream())
            {
                await request.Archive.CopyToAsync(memStream, cancellationToken);
                memStream.Position = 0;
                extractedFiles = archiveService.ExtractArchive(memStream, request.Archive.FileName);
            }

            var applicationTemplateEntry = extractedFiles.Where(x => x.Key == ApplicationConstants.ExportFileName).First();

            var applicationTemplates = JsonSerializer.Deserialize<ApplicationTemplate[]>(applicationTemplateEntry.Value);

            var dbTemplates = await db.ApplicationTemplates.Where(x => applicationTemplates.Select(y => y.Id).Contains(x.Id)).ToListAsync(cancellationToken);

            foreach (var applicationTemplate in applicationTemplates)
            {
                var dbTemplate = dbTemplates.SingleOrDefault(x => x.Id == applicationTemplate.Id);

                if (dbTemplate == null)
                {
                    var applicationTemplateEntity = mapper.Map<ApplicationTemplateEntity>(applicationTemplate);
                    db.ApplicationTemplates.Add(applicationTemplateEntity);
                }
                else
                {
                    if (request.OverWriteExisting)
                    {
                        mapper.Map(applicationTemplate, dbTemplate);
                    }
                    else
                    {
                        failures.Add($"{applicationTemplate.Name} ({applicationTemplate.Id})");
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            return new ImportApplicationTemplatesResult
            {
                Failures = failures.ToArray()
            };
        }
    }
}