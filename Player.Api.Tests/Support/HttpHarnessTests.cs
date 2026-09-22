// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Services;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Support;

/// <summary>
/// Tests for the HTTP harness itself. Every request test rests on these five facts, and a harness that
/// quietly routes to the wrong database or authorizes everything reads as a green suite.
/// </summary>
public class HttpHarnessTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>
    /// The unique index on view name is what makes the isolation check real: if two tests shared a
    /// database, whichever created this second would get a 500.
    /// </summary>
    private const string SharedName = "Http Isolation Probe";

    /// <summary>
    /// The application booted far enough to describe itself, which covers <c>AddSwagger</c> and the
    /// middleware ahead of the endpoints without touching a database.
    /// </summary>
    [Fact]
    public async Task The_swagger_document_is_served()
    {
        var response = await Client().GetAsync("/swagger/v1/swagger.json", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task A_request_with_no_identity_is_unauthorized()
    {
        var response = await Client().GetAsync("api/views", Ct);

        await AssertStatus(HttpStatusCode.Unauthorized, response);
    }

    /// <summary>
    /// An authenticated actor with no role reaches the handler and is refused there — so the claims
    /// transformer ran and derived nothing, rather than the pipeline granting by default.
    /// </summary>
    [Fact]
    public async Task An_actor_with_no_permissions_is_forbidden()
    {
        var actor = await Actor().SeedAsync();

        var response = await Client(actor).GetAsync("api/views", Ct);

        // ForbiddenException from BaseHandler, answered by ExceptionMiddleware rather than by the
        // authorization stack: the status and the problem body are its work.
        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    /// <summary>
    /// The other half: the seeded <c>Administrator</c> role becomes real permission claims, and the
    /// request reads the database this test seeded.
    /// </summary>
    [Fact]
    public async Task Root_reads_what_the_test_seeded()
    {
        var view = TestData.View();
        await Seed(view);

        var views = await ReadAsync<View[]>(await RootClient.GetAsync("api/views", Ct));

        Assert.Contains(view.Id, views.Select(x => x.Id));
    }

    /// <summary>
    /// The claims transformer creates the user row for a caller that has none, so a test does not have
    /// to seed the caller to make a request — <c>UserClaimsService.ValidateUser</c> with
    /// <c>update: true</c>.
    /// </summary>
    [Fact]
    public async Task A_caller_with_no_user_row_gets_one()
    {
        var stranger = new TestActor
        {
            Id = Guid.NewGuid(),
            Name = "Never Seeded",
            Memberships = []
        };

        await AssertStatus(HttpStatusCode.Forbidden, await Client(stranger).GetAsync("api/views", Ct));

        await using var context = NewContext();
        var user = await context.Users.SingleOrDefaultAsync(x => x.Id == stranger.Id, Ct);

        Assert.NotNull(user);
        Assert.Equal(stranger.Name, user.Name);
        Assert.Null(user.RoleId);
    }

    [Fact]
    public Task Concurrent_tests_share_the_host_but_not_the_database_first() => CreateSharedNameView();

    [Fact]
    public Task Concurrent_tests_share_the_host_but_not_the_database_second() => CreateSharedNameView();

    /// <summary>
    /// Entity events reach the application's real handlers: creating a view runs
    /// <c>ViewCreatedHandler</c>, which is only registered in the application's own container.
    /// </summary>
    /// <remarks>
    /// The webhook service is a run-wide substitute, so the assertion matches on a name unique to this
    /// test rather than counting calls.
    /// </remarks>
    [Fact]
    public async Task Entity_events_reach_the_real_handlers()
    {
        var name = $"Event Probe {Guid.NewGuid():N}";

        await ReadAsync<View>(await RootClient.PostAsJsonAsync(
            "api/views", new { name, status = ViewStatus.Active }, Ct));

        // The recorder is shared by the whole run, so the assertion names this view rather than counting
        // the events every other test raised through it.
        Assert.Single(Factory.Webhooks.Recorded(EventType.ViewCreated, x => x.Contains(name)));
    }

    private async Task CreateSharedNameView()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/views", new { name = SharedName, status = ViewStatus.Active }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);

        await using var context = NewContext();
        Assert.Equal(1, await context.Views.CountAsync(x => x.Name == SharedName, Ct));
    }
}
