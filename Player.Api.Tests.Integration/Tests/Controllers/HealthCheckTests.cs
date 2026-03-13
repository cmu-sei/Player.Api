// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Player.Api.Tests.Integration.Fixtures;
using Shouldly;
using Xunit;

namespace Player.Api.Tests.Integration.Tests.Controllers;

[Trait("Category", "Integration")]
public class HealthCheckTests : IClassFixture<PlayerTestContext>
{
    private readonly HttpClient _client;

    public HealthCheckTests(PlayerTestContext factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetLive_WhenHealthy_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/health/live");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetReady_WhenHealthy_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/health/ready");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
