// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Endpoints;

/// <summary>
/// What the API answers a client that puts something in a route segment that cannot be the type the
/// handler declares.
/// </summary>
/// <remarks>
/// <para>
/// This is a live question rather than a theoretical one because no route constrains its parameters:
/// <c>EndpointRegistrationTests.Route_parameters_come_from_a_known_vocabulary_and_carry_no_inline_constraints</c>
/// asserts every parameter policy list is empty. A <c>{id:guid}</c> would have the router decline the
/// match and answer 404; without it, the route matches and parameter binding is what fails.
/// </para>
/// <para>
/// Which matters because the two halves of the surface then answer the same mistake differently — the
/// minimal-API endpoints with a bare 400 and the controllers with <c>problem+json</c> — and the tests
/// here are where that is written down.
/// </para>
/// </remarks>
public class MalformedRouteParameterTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>A Guid that binds, for the cases where only one of two parameters is malformed.</summary>
    private const string Bindable = "11111111-1111-1111-1111-111111111111";

    /// <summary>
    /// Every shape a malformed segment comes in: a lone id, a trailing segment after it, either half of a
    /// two-parameter route, a write verb carrying a body, and an <c>int</c> rather than a Guid. All of them
    /// are a 400 with nothing in it — no problem document, no field name, no hint of which segment was
    /// rejected.
    /// </summary>
    /// <remarks>
    /// The empty body is the assertion worth reading. It is what minimal-API binding failure produces on
    /// its own, before any of the application's own error handling is reached, so a caller debugging a
    /// generated id has only the status code to go on. Related to issue 49, which is the same bare 400 for
    /// a missing query parameter.
    /// </remarks>
    [Theory]
    [InlineData("GET", "api/views/last-tuesday")]
    [InlineData("DELETE", "api/views/last-tuesday")]
    [InlineData("GET", "api/teams/last-tuesday")]
    [InlineData("GET", "api/views/last-tuesday/teams")]
    [InlineData("GET", $"api/users/last-tuesday/views/{Bindable}/teams")]
    [InlineData("GET", $"api/users/{Bindable}/views/last-tuesday/teams")]
    [InlineData("DELETE", $"api/views/{Bindable}/notifications/not-a-number")]
    public async Task A_route_parameter_that_will_not_bind_is_a_bad_request_with_no_body(
        string method,
        string route)
    {
        var response = await RootClient.SendAsync(Request(method, route), Ct);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// A write verb is the same answer, and the body it carried is never looked at — the malformed segment
    /// is decided first, so a client cannot tell this apart from a validation failure on what it sent.
    /// </summary>
    [Theory]
    [InlineData("POST", "api/views/last-tuesday/teams")]
    [InlineData("PUT", "api/teams/last-tuesday")]
    public async Task A_write_to_a_malformed_route_is_refused_before_its_body_is_read(
        string method,
        string route)
    {
        var response = await RootClient.SendAsync(
            Request(method, route, JsonContent.Create(new { name = "Blue" })), Ct);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// The controller half of the surface answers the same mistake as a <c>problem+json</c> document,
    /// because <c>[ApiController]</c> turns a model-binding failure into one. Two conventions for one
    /// client-visible error, decided by which era of the codebase the route came from.
    /// </summary>
    [Theory]
    [InlineData("GET", "api/files/last-tuesday")]
    [InlineData("GET", "api/files/download/last-tuesday")]
    [InlineData("DELETE", "api/files/last-tuesday")]
    [InlineData("GET", "api/views/last-tuesday/files")]
    public async Task The_controllers_answer_the_same_mistake_with_a_problem_document(
        string method,
        string route)
    {
        var problem = await AssertProblem(
            HttpStatusCode.BadRequest, await RootClient.SendAsync(Request(method, route), Ct));

        // The segment that could not bind is named, which is the whole difference from the bare 400 above.
        Assert.Contains("last-tuesday", string.Join(' ', problem.Extensions["errors"]?.ToString()));
    }

    /// <summary>
    /// Authentication runs before binding, so a malformed id from a caller with no identity is a 401 — an
    /// anonymous client cannot use a deliberately broken id to learn which routes exist.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_request_to_a_malformed_route_is_unauthorized()
    {
        await AssertStatus(
            HttpStatusCode.Unauthorized, await Client().GetAsync("api/views/last-tuesday", Ct));
    }

    private static HttpRequestMessage Request(string method, string route, HttpContent content = null) =>
        new(new HttpMethod(method), route) { Content = content };
}
