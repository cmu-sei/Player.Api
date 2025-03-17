// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.OpenApi.Models;
using Player.Api.Data.Data.Models;
using Player.Api.ViewModels.Webhooks;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Player.Api.Infrastructure.DocumentFilters
{
    public class ModelDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            context.SchemaGenerator.GenerateSchema(typeof(ViewCreated), context.SchemaRepository);
            context.SchemaGenerator.GenerateSchema(typeof(ViewDeleted), context.SchemaRepository);
            context.SchemaGenerator.GenerateSchema(typeof(WebhookEvent), context.SchemaRepository);
            context.SchemaGenerator.GenerateSchema(typeof(SystemPermission), context.SchemaRepository);
            context.SchemaGenerator.GenerateSchema(typeof(ViewPermission), context.SchemaRepository);
            context.SchemaGenerator.GenerateSchema(typeof(TeamPermission), context.SchemaRepository);
        }
    }
}