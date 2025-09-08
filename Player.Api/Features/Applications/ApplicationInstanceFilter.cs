// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Player.Api.Options;

namespace Player.Api.Features.Applications;

/// <summary>
/// Replace ApplicationInstance Uri properties with configured variables from appSettings, regardless of what endpoint returns them.
/// </summary>
public class ApplicationInstanceFilter(AppOptions options) : IEndpointFilter
{
    public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (options.UrlVariables.Count == 0)
        {
            return result;
        }

        switch (result)
        {
            case IValueHttpResult<ApplicationInstance> singleResult when singleResult.Value is not null:
                TransformApplication(singleResult.Value);
                break;

            case IValueHttpResult<IEnumerable<ApplicationInstance>> collectionResult when collectionResult.Value is not null:
                foreach (var app in collectionResult.Value)
                {
                    TransformApplication(app);
                }
                break;
        }

        return result;
    }

    private void TransformApplication(ApplicationInstance app)
    {
        app.Url = TransformString(app.Url);
        app.Icon = TransformString(app.Icon);
    }

    private string TransformString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var builder = new StringBuilder(input);
        foreach (var (key, value) in options.UrlVariables)
        {
            builder.Replace($"{{{key}}}", $"{value}");
        }

        return builder.ToString();
    }
}