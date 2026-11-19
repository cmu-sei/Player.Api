// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authentication;

/// <summary>
/// <c>Startup</c>'s bearer options read a hub connection's token from <c>access_token</c>, where the SignalR
/// client's <c>accessTokenFactory</c> puts it, so a reconnect authenticates with the client's current token.
/// The test host swaps the bearer handler for its own, so these run the configured event directly.
/// </summary>
public class HubAccessTokenTests(PlayerAppFactory factory)
{
    [Theory]
    [InlineData("/hubs/team")]
    [InlineData("/hubs/view/negotiate")]
    public async Task A_hub_request_takes_its_token_from_access_token(string path)
    {
        Assert.Equal("hub-token", await TokenRead(path, "?access_token=hub-token"));
    }

    [Theory]
    [InlineData("/api/views")]
    [InlineData("/hubsx/team")]
    [InlineData("/api/hubs/team")]
    public async Task Other_requests_ignore_access_token(string path)
    {
        Assert.Null(await TokenRead(path, "?access_token=hub-token"));
    }

    [Theory]
    [InlineData("?bearer=old-token")]
    [InlineData("?access_token=")]
    public async Task A_hub_request_without_access_token_is_left_to_the_authorization_header(string query)
    {
        Assert.Null(await TokenRead("/hubs/team", query));
    }

    /// <summary>
    /// A client whose hub url still carries the token it loaded with, as <c>?bearer=</c> (which the middleware
    /// copies into the Authorization header), reconnects with the factory's current token.
    /// </summary>
    [Fact]
    public async Task Access_token_wins_over_the_authorization_header()
    {
        Assert.Equal("fresh-token",
            await TokenRead("/hubs/team", "?bearer=stale-token&access_token=fresh-token", "Bearer stale-token"));
    }

    /// <summary>The client appends the factory's token, so a url that already carried one gives two.</summary>
    [Fact]
    public async Task The_last_access_token_is_the_one_read()
    {
        Assert.Equal("fresh-token", await TokenRead("/hubs/team", "?access_token=stale-token&access_token=fresh-token"));
    }

    private async Task<string> TokenRead(string path, string query, string authorization = null)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(query);
        if (authorization != null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }
        var context = new MessageReceivedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options);

        await options.Events.OnMessageReceived(context);

        return context.Token;
    }
}
