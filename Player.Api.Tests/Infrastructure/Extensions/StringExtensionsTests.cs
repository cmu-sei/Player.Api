// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Extensions;

namespace Player.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// <c>ToUri</c> decides which stored urls the application will fetch — it gates the icon downloads in
/// the application template export.
/// </summary>
public class StringExtensionsTests
{
    [Theory]
    [InlineData("https://example.test/icon.png")]
    [InlineData("http://example.test/icon.png")]
    public void ToUri_accepts_an_absolute_http_url(string value)
    {
        Assert.Equal(value, value.ToUri()?.ToString());
    }

    /// <summary>
    /// A bare host is the shape a hand-entered url takes, so http is assumed rather than rejected.
    /// </summary>
    [Fact]
    public void ToUri_prepends_a_scheme_to_a_www_host()
    {
        Assert.Equal("http://www.example.test/", "www.example.test".ToUri()?.ToString());
    }

    /// <summary>
    /// Only the www case is guessed at. Anything else without a scheme stays rejected.
    /// </summary>
    [Fact]
    public void ToUri_does_not_prepend_a_scheme_to_another_host()
    {
        Assert.Null("example.test".ToUri());
    }

    /// <summary>
    /// Restricted to http, so a stored value cannot make the server read a local file or open an
    /// unexpected protocol.
    /// </summary>
    [Theory]
    [InlineData("ftp://example.test/icon.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:image/png;base64,AAAA")]
    public void ToUri_rejects_a_non_http_scheme(string value)
    {
        Assert.Null(value.ToUri());
    }

    [Theory]
    [InlineData("/assets/icon.png")]
    [InlineData("icon.png")]
    [InlineData("not a url at all")]
    public void ToUri_rejects_a_value_that_is_not_absolute(string value)
    {
        Assert.Null(value.ToUri());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToUri_rejects_a_missing_value(string value)
    {
        Assert.Null(value.ToUri());
    }
}
