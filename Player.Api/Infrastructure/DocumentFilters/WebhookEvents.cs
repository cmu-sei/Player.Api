// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.OpenApi.Models;
using Player.Api.ViewModels.Webhooks;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Player.Api.Infrastructure.DocumentFilters
{
    public class WebhookDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {

            context.SchemaGenerator.GenerateSchema(typeof(ViewCreated), context.SchemaRepository);
            context.SchemaGenerator.GenerateSchema(typeof(ViewDeleted), context.SchemaRepository);
        }
    }
}