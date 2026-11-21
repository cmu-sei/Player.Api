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
    /// Stands in for each placeholder while the url is checked. Control characters are refused before
    /// it is used, so it cannot be mistaken for one the url holds.
    /// </summary>
    private const char Hole = '\0';

    /// <summary>
    /// A placeholder: <c>{viewId}</c>, <c>{teamName}</c>, <c>{theme}</c>, an <c>Applications:UrlVariables</c>
    /// key, or any other text in braces, which is left as it is when nothing fills it in.
    /// </summary>
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();

    /// <summary>A scheme, as a browser reads one before the first colon of a url.</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*$")]
    private static partial Regex Scheme();

    /// <summary>
    /// Whether <paramref name="url"/> may be saved as an Application's or Application Template's url. No
    /// url is allowed, as an Application inherits its template's.
    /// </summary>
    /// <remarks>
    /// A placeholder is filled in after this check, some of them with names users choose, so one before
    /// the first colon is assumed to become whatever completes another scheme: any letters, or nothing.
    /// Nothing matters as much as letters, since browsers strip the spaces that then lead the url:
    /// <c>{teamName} javascript:</c> is <c>javascript:</c> for a team with no name. The filled-in values
    /// cannot add a colon, a space or a control character themselves: the view and team names are
    /// escaped with <see cref="Uri.EscapeDataString(string)"/>, the ids are guids, <c>{theme}</c> becomes
    /// a query parameter, and the url variables are the operator's.
    /// </remarks>
    public static bool IsAllowed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        // Browsers strip the spaces and control characters around a url, so a pasted one may keep its
        // newline. Inside, they drop tabs and newlines, which can put a scheme back together
        // ("java\tscript:").
        url = url.Trim();
        if (url.Any(char.IsControl))
            return false;

        var template = Placeholder().Replace(url, Hole.ToString());

        // Only an operator's url variable can put a colon in, so the url's own first colon is where any
        // scheme a user could choose ends.
        var colon = template.IndexOf(':');
        if (colon < 0)
            return true;

        var prefix = template[..colon];

        if (!prefix.Contains(Hole))
            return !Scheme().IsMatch(prefix) ||
                prefix.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        // With placeholders in it, the prefix can only be kept from becoming a scheme by text of its own
        // that no scheme may hold. Spaces the placeholders could leave at the start do not count.
        return prefix.Replace(Hole.ToString(), string.Empty).TrimStart().Any(c => !IsSchemeCharacter(c));
    }

    private static bool IsSchemeCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '+' or '.' or '-';
}
