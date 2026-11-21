// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Features.Applications;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// <see cref="ApplicationUrls.IsAllowed"/> decides which urls Player will load into an iframe. The
/// create, edit and import paths that call it are covered in their own request and importer tests.
/// </summary>
public class ApplicationUrlsTests
{
    /// <summary>
    /// No url is allowed, as an Application with none takes its template's.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_url_is_allowed(string url)
    {
        Assert.True(ApplicationUrls.IsAllowed(url));
    }

    /// <summary>
    /// The shapes Player's own seed data, Blueprint and the operator url variables produce: absolute
    /// http(s) urls with placeholders anywhere, including as the whole address, and relative urls.
    /// </summary>
    [Theory]
    [InlineData("https://example.test/app")]
    [InlineData("HTTP://example.test/app")]
    [InlineData("http://localhost:4303/views/{viewId}?{theme}")]
    [InlineData("http://localhost:4303/views/{viewId}/map{theme}")]
    [InlineData("https://{vm-ui}/views/{viewId}")]
    [InlineData("https://{host}:{port}/views/{viewId}")]
    [InlineData("https://{console-ui}/?vm=https://{vm-ui}/&fallback=https://{vm-ui}/")]
    [InlineData("https://console.example.com/user/{userId}/view/{viewId}/console")]
    [InlineData("{vmUrl}/views/{playerViewId}{theme}")]
    [InlineData("{galleryUrl}?section=archive&exhibit={galleryExhibitId}{theme}")]
    [InlineData("/vm/views/{viewId}")]
    [InlineData("assets/app/index.html")]
    [InlineData("./app?team={teamName}")]
    [InlineData("//example.test/app")]
    [InlineData("/vm/{viewId}/notes:today")]
    [InlineData("{vmUrl}?at=12:00")]
    [InlineData(" https://example.test/app ")]
    public void An_http_or_relative_url_is_allowed(string url)
    {
        Assert.True(ApplicationUrls.IsAllowed(url));
    }

    /// <summary>
    /// Any other scheme is refused, whatever its case or surrounding spaces. <c>javascript:</c> in an
    /// iframe's src runs in Player's own origin.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(document.domain)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("blob:https://example.test/0b5e")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.test/app")]
    [InlineData("about:blank")]
    [InlineData("view-source:https://example.test")]
    public void Another_scheme_is_refused(string url)
    {
        Assert.False(ApplicationUrls.IsAllowed(url));
    }

    /// <summary>
    /// A pasted url can bring a newline or tab with it. Browsers strip those from the ends, so they are
    /// allowed there.
    /// </summary>
    [Theory]
    [InlineData("https://example.test/app\n")]
    [InlineData("\thttps://example.test/app\r\n")]
    [InlineData("{vmUrl}/views/{viewId}\n")]
    public void Whitespace_around_a_url_is_allowed(string url)
    {
        Assert.True(ApplicationUrls.IsAllowed(url));
    }

    /// <summary>
    /// Inside a url, browsers drop tabs and newlines, and they strip other control characters from its
    /// ends, so any of them could hide a scheme from a check that reads the text as stored.
    /// </summary>
    [Theory]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\tscript:alert(1)\n")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("\u0001javascript:alert(1)")]
    [InlineData("https://example.test/\r\napp")]
    public void A_url_with_a_control_character_is_refused(string url)
    {
        Assert.False(ApplicationUrls.IsAllowed(url));
    }

    /// <summary>
    /// A placeholder is filled in later, some of them from names users choose, so one before the first
    /// colon is assumed to complete another scheme, with letters or with nothing. A team named
    /// "javascript" turns the first url into <c>javascript:alert(1)</c>; a team or view with no name
    /// turns the next ones into it too, the spaces left in front being stripped by the browser.
    /// </summary>
    [Theory]
    [InlineData("{teamName}:alert(1)")]
    [InlineData("{teamName} javascript:alert(document.domain)")]
    [InlineData("{viewName}javascript:alert(1)")]
    [InlineData("{a}{b}javascript:alert(1)")]
    [InlineData("{viewName} {teamName}:alert(1)")]
    [InlineData("{not:a:variable}javascript:alert(1)")]
    [InlineData("java{viewName}script:alert(1)")]
    [InlineData("{scheme}://example.test/app")]
    [InlineData("http{s}://example.test/app")]
    public void A_placeholder_in_the_scheme_is_refused(string url)
    {
        Assert.False(ApplicationUrls.IsAllowed(url));
    }
}
