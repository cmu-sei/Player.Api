// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Controllers;

/// <summary>
/// Covers the two health routes over HTTP. Every request here is unauthenticated, which is the point:
/// <c>HealthController</c> is <c>[AllowAnonymous]</c> so that an orchestrator can probe the service
/// without a token.
/// </summary>
/// <remarks>
/// Each path is registered twice — by <c>HealthController</c> through <c>MapControllers</c>, and by
/// <c>MapHealthChecks</c> at <c>Startup.cs:321,326</c>. They are not in conflict because only the
/// controller constrains the method: routing prefers the endpoint that names <c>GET</c>, and the
/// <c>MapHealthChecks</c> registration answers everything else. The two return different bodies, which
/// is how these tests tell which one replied.
/// </remarks>
public class HealthControllerTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>
    /// The liveness check is the configured provider's, tagged <c>live</c> in <c>Startup</c>. The test
    /// host's provider is SQLite over <c>:memory:</c>, and this is the only test that opens it.
    /// </summary>
    [Fact]
    public async Task Live_reports_healthy()
    {
        var response = await Client().GetAsync("api/health/live", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(HealthStatus.Healthy, await ReadAsync<HealthStatus>(response));
    }

    [Fact]
    public async Task Ready_reports_healthy()
    {
        var response = await Client().GetAsync("api/health/ready", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(HealthStatus.Healthy, await ReadAsync<HealthStatus>(response));
    }

    /// <summary>
    /// A JSON body is the controller's answer: it serializes the status through MVC, which
    /// <c>Startup</c> gives the string enum converter. The <c>MapHealthChecks</c> writer would have
    /// answered <c>text/plain</c>.
    /// </summary>
    [Fact]
    public async Task A_get_is_answered_by_the_controller()
    {
        var response = await Client().GetAsync("api/health/live", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"Healthy\"", await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// The controller only answers <c>GET</c>, so anything else falls through to the
    /// <c>MapHealthChecks</c> endpoint — which has no method constraint and reports the same status as
    /// plain text rather than the 405 a lone controller route would give.
    /// </summary>
    [Fact]
    public async Task A_post_is_answered_by_the_mapped_health_check_instead()
    {
        var response = await Client().PostAsync("api/health/live", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }
}
