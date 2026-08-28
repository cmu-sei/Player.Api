// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Constants;

namespace Player.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// The CORS policy is the browser-facing half of the deployment's configuration: the UI reaches this API
/// cross-origin, so a policy built wrong takes the whole front end down.
/// </summary>
public class CorsPolicyExtensionsTests
{
    [Fact]
    public void Build_uses_the_configured_lists()
    {
        var policy = new CorsPolicyOptions
        {
            Origins = ["https://player.test"],
            Methods = ["GET", "POST"],
            Headers = ["Authorization"]
        }.Build();

        Assert.Equal(["https://player.test"], policy.Origins);
        Assert.Equal(["GET", "POST"], policy.Methods);
        Assert.Equal(["Authorization"], policy.Headers);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.AllowAnyMethod);
        Assert.False(policy.AllowAnyHeader);
    }

    [Fact]
    public void Build_honors_the_allow_any_flags()
    {
        var policy = new CorsPolicyOptions
        {
            AllowAnyOrigin = true,
            AllowAnyMethod = true,
            AllowAnyHeader = true
        }.Build();

        Assert.True(policy.AllowAnyOrigin);
        Assert.True(policy.AllowAnyMethod);
        Assert.True(policy.AllowAnyHeader);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_carries_the_configured_credentials_setting(bool supportsCredentials)
    {
        var policy = new CorsPolicyOptions
        {
            Origins = ["https://player.test"],
            AllowAnyMethod = true,
            AllowAnyHeader = true,
            SupportsCredentials = supportsCredentials
        }.Build();

        Assert.Equal(supportsCredentials, policy.SupportsCredentials);
    }

    /// <summary>
    /// The CORS protocol forbids a wildcard origin alongside credentials, and the builder enforces it —
    /// so this misconfiguration fails at startup rather than serving a policy no browser will honor.
    /// </summary>
    [Fact]
    public void Build_rejects_credentials_with_a_wildcard_origin()
    {
        var options = new CorsPolicyOptions
        {
            AllowAnyOrigin = true,
            AllowAnyMethod = true,
            AllowAnyHeader = true,
            SupportsCredentials = true
        };

        Assert.Throws<InvalidOperationException>(() => options.Build());
    }

    /// <summary>
    /// Both headers are exposed regardless of configuration. A browser cannot read an unexposed response
    /// header, so without these a cross-origin download loses its filename and an export's error flag
    /// goes unnoticed.
    /// </summary>
    [Fact]
    public void Build_always_exposes_the_download_headers()
    {
        var policy = new CorsPolicyOptions { AllowAnyOrigin = true, AllowAnyMethod = true, AllowAnyHeader = true }
            .Build();

        Assert.Contains(HttpConstants.ContentDispositionHeader, policy.ExposedHeaders);
        Assert.Contains(HttpConstants.ArchiveErrorsHeader, policy.ExposedHeaders);
    }

    /// <summary>
    /// Bound from configuration rather than constructed, since that is how it arrives at startup.
    /// </summary>
    [Fact]
    public void UseConfiguredCors_registers_the_default_policy_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Origins:0"] = "https://player.test",
                ["Methods:0"] = "GET",
                ["Headers:0"] = "Authorization",
                ["SupportsCredentials"] = "true"
            })
            .Build();

        var options = new CorsOptions().UseConfiguredCors(configuration);

        var policy = options.GetPolicy("default");
        Assert.NotNull(policy);
        Assert.Equal(["https://player.test"], policy.Origins);
        Assert.True(policy.SupportsCredentials);
    }
}
