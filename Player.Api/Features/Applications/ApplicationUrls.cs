// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Player.Api.Features.Applications;

/// <summary>
/// An Application's url is loaded into an iframe in Player, so only http and https urls are accepted, or
/// urls with no scheme at all, which the browser resolves against Player's own address. Any other scheme
/// is refused: a <c>javascript:</c> url would run in Player's origin. Urls are templates, so placeholders
/// such as <c>{viewId}</c>, <c>{teamName}</c>, <c>{theme}</c> or an <c>Applications:UrlVariables</c> key
/// are allowed anywhere, including in place of the whole address (<c>{vmUrl}/views/{viewId}</c>).
/// </summary>
public static partial class ApplicationUrls
{
    public const string InvalidMessage =
        "An Application's Url must be an absolute http or https URL, or a relative one.";

    /// <summary>
    /// A placeholder. It stands for text that is only known later, some of it chosen by users (a team
    /// name), so it is checked as a letter: one in front of a colon then reads as a scheme of its own,
    /// which is refused, rather than as a relative path. <c>{teamName}:alert(1)</c> would otherwise pass
    /// and become <c>javascript:alert(1)</c> for a team named "javascript".
    /// </summary>
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();

    /// <summary>A scheme and its colon, as a browser reads one at the start of a url.</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex Scheme();

    /// <summary>
    /// Whether <paramref name="url"/> may be saved as an Application's or Application Template's url. No
    /// url is allowed, as an Application inherits its template's.
    /// </summary>
    public static bool IsAllowed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        // Browsers drop tabs and newlines anywhere in a url, which can put a scheme back together
        // ("java\tscript:"), and strip other control characters from its ends.
        if (url.Any(char.IsControl))
            return false;

        var scheme = Scheme().Match(Placeholder().Replace(url.Trim(), "x"));

        return !scheme.Success ||
            scheme.Value.Equals($"{Uri.UriSchemeHttp}:", StringComparison.OrdinalIgnoreCase) ||
            scheme.Value.Equals($"{Uri.UriSchemeHttps}:", StringComparison.OrdinalIgnoreCase);
    }
}
