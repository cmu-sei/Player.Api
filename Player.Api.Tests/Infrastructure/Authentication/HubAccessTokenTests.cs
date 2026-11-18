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
public class HubAccessTokenTests(DatabaseFixture fixture, PlayerAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Theory]
    [InlineData("/hubs/team")]
    [InlineData("/hubs/view/negotiate")]
    public async Task A_hub_request_takes_its_token_from_access_token(string path)
    {
        Assert.Equal("hub-token", await TokenRead(path, "?access_token=hub-token"));
    }

    [Fact]
    public async Task Other_requests_ignore_access_token()
    {
        Assert.Null(await TokenRead("/api/views", "?access_token=hub-token"));
    }

    [Fact]
    public async Task A_hub_request_without_access_token_is_left_to_the_authorization_header()
    {
        Assert.Null(await TokenRead("/hubs/team", "?bearer=old-token"));
    }

    private async Task<string> TokenRead(string path, string query)
    {
        var options = Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(query);
        var context = new MessageReceivedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options);

        await options.Events.OnMessageReceived(context);

        return context.Token;
    }
}
