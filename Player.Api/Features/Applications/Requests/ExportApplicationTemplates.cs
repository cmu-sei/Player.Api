// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Constants;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Services;

namespace Player.Api.Features.Applications;

public class ExportApplicationTemplates
{
    [DataContract(Name = "ExportApplicationTemplatesQuery")]
    public record Query : IRequest<ArchiveResult>
    {
        public Guid[] Ids { get; set; }
        public bool IncludeIcons { get; set; }
        public bool EmbedIcons { get; set; }
        public ArchiveType ArchiveType { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("application-templates/actions/export", TypedHandler)
                    .WithName("exportApplicationTemplates")
                    .WithDescription("Returns an archive of Application Templates and supporting files.")
                    .WithSummary("Export Application Templates.")
                    .Produces(StatusCodes.Status200OK, typeof(FileResult), "application/octet-stream")
            ];
        }

        async Task<FileStreamHttpResult> TypedHandler(IMediator mediator, HttpContext httpContext, [AsParameters] Query query, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(query, cancellationToken);

            if (result.HasErrors)
            {
                httpContext.Response.Headers.Append(HttpConstants.ArchiveErrorsHeader, "true");
            }

            return TypedResults.File(result.Data, result.Type, result.Name);
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IArchiveService archiveService, IMapper mapper, IHttpClientFactory httpClientFactory) : BaseHandler<Query, ArchiveResult>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewApplications], [], [], cancellationToken);

        public override async Task<ArchiveResult> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            IQueryable<ApplicationTemplateEntity> query = db.ApplicationTemplates;

            if (request.Ids.Any())
            {
                query = query.Where(x => request.Ids.Contains(x.Id));
            }

            var items = await query
                .ProjectTo<ApplicationTemplate>(mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            var data = new Dictionary<string, object>();
            var errors = new StringBuilder();

            if (request.IncludeIcons)
            {
                var httpClient = httpClientFactory.CreateClient();

                var tasks = items
                    .Select(item => (item, uri: item.Icon.ToUri()))
                    .Where(x => x.uri != null)
                    .Select(async item =>
                    {
                        HttpResponseMessage response = null;

                        try
                        {
                            response = await httpClient.GetAsync(item.uri, cancellationToken);
                            return (item.item, item.uri, response, null);
                        }
                        catch (Exception ex)
                        {
                            return (item.item, item.uri, response, ex);
                        }
                    })
                    .ToList();

                var results = await Task.WhenAll(tasks);

                foreach (var (item, uri, response, ex) in results)
                {
                    if (ex != null || response == null || !response.IsSuccessStatusCode)
                    {
                        errors.AppendLine($"Name: {item.Name}");
                        errors.AppendLine($"Icon: {item.Icon}");
                        errors.AppendLine($"Error: {(response != null ? response.ReasonPhrase : ex.Message)}");
                        errors.AppendLine();
                        continue;
                    }

                    var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                    if (request.EmbedIcons)
                    {
                        var contentType = response.Content.Headers.ContentType?.MediaType ?? GetMimeType(uri.LocalPath);
                        var base64 = Convert.ToBase64String(imageBytes);
                        item.Icon = $"data:{contentType};base64,{base64}";
                    }
                    else
                    {
                        data[item.Icon.Replace('/', '_')] = imageBytes;
                    }
                }
            }

            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions()
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Doesn't escape characters. Will break data urls otherwise
            });
            data.Add(ApplicationConstants.ExportFileName, json);

            if (errors.Length > 0)
            {
                data.Add(ApplicationConstants.ErrorFileName, errors.ToString());
            }

            var archive = await archiveService.ArchiveData(ApplicationConstants.ExportFileName, request.ArchiveType, data);

            if (errors.Length > 0)
            {
                archive.HasErrors = true;
            }

            return archive;
        }

        private string GetMimeType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".ico" => "image/x-icon",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
        }
    }
}