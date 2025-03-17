// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Player.Api.Infrastructure.Endpoints;

public interface IEndpoint
{
    RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group);
    RouteHandlerBuilder GroupEndpoints(RouteHandlerBuilder builder, string groupName)
    {
        return builder.WithTags(groupName.Substring(0, groupName.Length - 1));
    }
}