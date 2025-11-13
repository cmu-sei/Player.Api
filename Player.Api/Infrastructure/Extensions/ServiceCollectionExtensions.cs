// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Player.Api.Infrastructure.OperationFilters;
using Player.Api.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using System.Text.Json;
using Player.Api.Infrastructure.DocumentFilters;
using System.Runtime.Serialization;
using Microsoft.OpenApi;

namespace Player.Api.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddSwagger(this IServiceCollection services, AuthorizationOptions authOptions)
        {
            // XML Comments path
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string commentsFileName = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
            string commentsFile = Path.Combine(baseDirectory, commentsFileName);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Player API", Version = "v1" });
                c.CustomSchemaIds(schemaIdStrategy);

                c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authOptions.AuthorizationUrl),
                            TokenUrl = new Uri(authOptions.TokenUrl),
                            Scopes = new Dictionary<string, string>()
                            {
                                {authOptions.AuthorizationScope, "public api access"}
                            }
                        }
                    }
                });

                c.AddSecurityRequirement((document) => new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("oauth2", document), [authOptions.AuthorizationScope] }
                });

                c.IncludeXmlComments(commentsFile);
                c.EnableAnnotations();
                c.OperationFilter<DefaultResponseOperationFilter>();
                c.DocumentFilter<ModelDocumentFilter>();
                c.MapType<Optional<Guid?>>(() => new OpenApiSchema
                {
                    OneOf = new List<IOpenApiSchema>
                    {
                        new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                        new OpenApiSchema { Type = JsonSchemaType.Null }
                    }
                });

                c.MapType<JsonElement?>(() => new OpenApiSchema
                {
                    OneOf = new List<IOpenApiSchema>
                    {
                        new OpenApiSchema { Type = JsonSchemaType.Object },
                        new OpenApiSchema { Type = JsonSchemaType.Null }
                    }
                });
            });
        }

        private static string schemaIdStrategy(Type currentClass)
        {
            var dataContractAttribute = currentClass.GetCustomAttribute<DataContractAttribute>();
            return dataContractAttribute != null && dataContractAttribute.Name != null ? dataContractAttribute.Name : currentClass.Name;
        }
    }
}
