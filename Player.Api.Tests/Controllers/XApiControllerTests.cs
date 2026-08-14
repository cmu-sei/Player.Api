// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Microsoft.EntityFrameworkCore;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Controllers;

/// <summary>
/// Covers <c>XApiController</c> over HTTP: its four routes, their parameter binding, and the
/// short-circuit every one of them starts with.
/// </summary>
/// <remarks>
/// xAPI is off for the whole run — <c>appsettings.json</c> ships <c>XApiOptions:Enabled</c> false and
/// an empty <c>Username</c>, and one host serves every test, so no test can turn it on without turning
/// it on for all of them. What that leaves reachable here is the <c>IsConfigured</c> guard, which is
/// the branch a deployment with xAPI switched off takes. Emission itself is covered directly by
/// <c>XApiServiceTests</c> and <c>XApiQueueServiceTests</c>.
/// </remarks>
public class XApiControllerTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>
    /// The ids are never looked up: the guard returns before the service reads anything, so a route
    /// naming a view that does not exist is still a 200.
    /// </summary>
    [Fact]
    public async Task Viewed_is_accepted_and_records_nothing()
    {
        var response = await RootClient.PostAsync($"api/xapi/viewed/view/{Guid.NewGuid()}", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await AssertNothingQueued();
    }

    [Fact]
    public async Task Application_switched_is_accepted_and_records_nothing()
    {
        var response = await RootClient.PostAsync(
            $"api/xapi/experienced/view/{Guid.NewGuid()}/application" +
            "?applicationName=Console&applicationUrl=https://example.test/console",
            null,
            Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await AssertNothingQueued();
    }

    [Fact]
    public async Task Terminated_is_accepted_and_records_nothing()
    {
        var response = await RootClient.PostAsync(
            $"api/xapi/terminated/view/{Guid.NewGuid()}?durationSeconds=90", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await AssertNothingQueued();
    }

    [Fact]
    public async Task Team_switched_is_accepted_and_records_nothing()
    {
        var response = await RootClient.PostAsync(
            $"api/xapi/switched/view/{Guid.NewGuid()}/team/{Guid.NewGuid()}", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await AssertNothingQueued();
    }

    /// <summary>
    /// Both query parameters are optional strings, so a client that sends neither is accepted and the
    /// service would be handed nulls.
    /// </summary>
    [Fact]
    public async Task Application_switched_accepts_a_request_with_neither_query_parameter()
    {
        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.PostAsync(
                $"api/xapi/experienced/view/{Guid.NewGuid()}/application", null, Ct));
    }

    /// <summary>
    /// <c>durationSeconds</c> is a non-nullable <c>int</c> with no default, which binds to zero when
    /// the query string omits it rather than failing the request.
    /// </summary>
    [Fact]
    public async Task Terminated_accepts_a_request_with_no_duration()
    {
        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.PostAsync($"api/xapi/terminated/view/{Guid.NewGuid()}", null, Ct));
    }

    /// <summary>
    /// A value that will not bind is answered by <c>[ApiController]</c> before the action runs, as a
    /// <c>problem+json</c> 400.
    /// </summary>
    [Fact]
    public async Task Terminated_is_a_bad_request_for_a_duration_that_is_not_a_number()
    {
        await AssertProblem(
            HttpStatusCode.BadRequest,
            await RootClient.PostAsync(
                $"api/xapi/terminated/view/{Guid.NewGuid()}?durationSeconds=soon", null, Ct));
    }

    [Fact]
    public async Task A_view_id_that_is_not_a_guid_is_a_bad_request()
    {
        await AssertProblem(
            HttpStatusCode.BadRequest,
            await RootClient.PostAsync("api/xapi/viewed/view/last-tuesday", null, Ct));
    }

    /// <summary>
    /// The controller carries its own <c>[Authorize]</c> rather than inheriting <c>BaseController</c>,
    /// so this pins that it is not an anonymous route.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_request_is_unauthorized()
    {
        await AssertStatus(
            HttpStatusCode.Unauthorized,
            await Client().PostAsync($"api/xapi/viewed/view/{Guid.NewGuid()}", null, Ct));
    }

    /// <summary>
    /// Emitting a statement queues a row, so an empty queue is how a test says nothing was emitted.
    /// </summary>
    private async Task AssertNothingQueued()
    {
        await using var db = NewContext();

        Assert.Empty(await db.XApiQueuedStatements.ToListAsync(Ct));
    }
}
