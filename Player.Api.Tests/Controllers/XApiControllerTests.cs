// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Player.Api.Controllers;
using Player.Api.Services;
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

/// <summary>
/// The other side of the guard: what each action does once xAPI *is* configured.
/// </summary>
/// <remarks>
/// <para>
/// Not over HTTP, deliberately. <c>XApiOptions:Enabled</c> is configuration and one host serves the whole
/// run, so turning it on for one test turns it on for every test; a second host would pin the
/// configuration rather than the controller. The controller has no state and one dependency, so calling
/// the actions directly against a substituted <see cref="IXApiService"/> asks exactly what is left to
/// ask — which arguments each route hands the service, in which order and in which unit.
/// </para>
/// <para>
/// The substitute is created per test instance rather than shared, which is what keeps NSubstitute's
/// per-thread call bookkeeping from crossing between tests running in parallel.
/// </para>
/// </remarks>
public class XApiControllerEmissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IXApiService _xApi = Substitute.For<IXApiService>();

    [Fact]
    public async Task ViewViewed_emits_for_the_view_in_the_route()
    {
        var viewId = Guid.NewGuid();

        Assert.IsType<OkResult>(await Controller().ViewViewed(viewId, Ct));

        await _xApi.Received(1).EmitViewViewedAsync(viewId, Ct);
    }

    /// <summary>
    /// Both query values reach the service, and in that order — they are two strings of the same type,
    /// so nothing but a test distinguishes them from each other.
    /// </summary>
    [Fact]
    public async Task Application_switched_emits_the_name_and_the_url_it_was_given()
    {
        var viewId = Guid.NewGuid();

        Assert.IsType<OkResult>(await Controller().ApplicationSwitched(
            viewId, "Console", "https://example.test/console", Ct));

        await _xApi.Received(1).EmitApplicationSwitchedAsync(
            viewId, "Console", "https://example.test/console", Ct);
    }

    /// <summary>
    /// The controller's only transformation: the route's <c>int</c> of seconds becomes the service's
    /// <see cref="TimeSpan"/>. Zero is what an omitted <c>durationSeconds</c> binds to, and the negative
    /// case says the controller validates nothing — a client can report a view that ended before it
    /// started.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(-30)]
    public async Task View_terminated_emits_the_duration_as_seconds(int seconds)
    {
        var viewId = Guid.NewGuid();

        Assert.IsType<OkResult>(await Controller().ViewTerminated(viewId, seconds, Ct));

        await _xApi.Received(1).EmitViewTerminatedAsync(viewId, TimeSpan.FromSeconds(seconds), Ct);
    }

    /// <summary>
    /// View first, then team. Worth its own assertion because the neighbouring
    /// <c>EmitTeamJoinedAsync(teamId, viewId)</c> takes the same two Guids in the opposite order, so a
    /// transposition here compiles and mislabels every statement.
    /// </summary>
    [Fact]
    public async Task Team_switched_emits_the_view_before_the_team()
    {
        var viewId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        Assert.IsType<OkResult>(await Controller().TeamSwitched(viewId, teamId, Ct));

        await _xApi.Received(1).EmitTeamSwitchedAsync(viewId, teamId, Ct);
    }

    /// <summary>
    /// The complement of <see cref="XApiControllerTests"/>'s four routes: those assert that nothing was
    /// queued, which is what a caller can see, and this asserts that the service was never asked, which
    /// is what the guard is for.
    /// </summary>
    [Fact]
    public async Task Nothing_is_emitted_when_the_service_is_not_configured()
    {
        var controller = Controller(configured: false);

        Assert.IsType<OkResult>(await controller.ViewViewed(Guid.NewGuid(), Ct));
        Assert.IsType<OkResult>(await controller.ApplicationSwitched(Guid.NewGuid(), "Console", null, Ct));
        Assert.IsType<OkResult>(await controller.ViewTerminated(Guid.NewGuid(), 90, Ct));
        Assert.IsType<OkResult>(await controller.TeamSwitched(Guid.NewGuid(), Guid.NewGuid(), Ct));

        // Every call the substitute saw was the guard itself, so no action got past it.
        Assert.Equal(4, _xApi.ReceivedCalls().Count());
        Assert.All(_xApi.ReceivedCalls(), x => Assert.Equal(nameof(IXApiService.IsConfigured), x.GetMethodInfo().Name));
    }

    private XApiController Controller(bool configured = true)
    {
        _xApi.IsConfigured().Returns(configured);
        return new XApiController(_xApi);
    }
}
