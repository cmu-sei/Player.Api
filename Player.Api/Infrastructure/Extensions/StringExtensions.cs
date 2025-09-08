// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Player.Api.Extensions;

public static class StringExtensions
{

    /// <summary>
    /// Checks if the string is a valid Uri. Adds http if missing a scheme
    /// </summary>
    /// <param name="uri"></param>
    /// <returns>A Uri if valid, otherwise null</returns>
    public static Uri ToUri(this string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        // If it starts with www, prepend http://
        if (!uri.Contains("://") && uri.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            uri = "http://" + uri;
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri result)
               && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps))
        {
            return result;
        }

        return null;
    }
}