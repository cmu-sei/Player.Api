// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Endpoints;

/// <summary>
/// What the API answers a client whose request body is missing, is not JSON, or is JSON that cannot be the
/// command the handler declares — the body half of what
/// <see cref="MalformedRouteParameterTests"/> covers for route segments.
/// </summary>
/// <remarks>
/// <para>
/// The headline is the first theory: a write with no body at all is a 500, on every route that takes one.
/// Nullable reference types are not enabled in <c>Player.Api</c>, so a reference-typed body parameter is
/// nullable-oblivious, and minimal-API binding treats that as optional — the absent body becomes
/// <c>null</c> and the handler is called with it rather than the request being refused. Issue 63.
/// </para>
/// <para>
/// Everything else here is the framework answering before any application code runs, which is worth
/// pinning precisely because it is not the application's own error shape: no problem document, no field
/// name, and a 415 rather than a 400 for a content type the route cannot read.
/// </para>
/// </remarks>
public class MalformedRequestBodyTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>A Guid that binds, so the route is matched and the body is what fails.</summary>
    private const string Bindable = "11111111-1111-1111-1111-111111111111";

    /// <summary>What MediatR's own null guard says, for the handlers that pass the command straight on.</summary>
    private const string MediatorRefused = "Value cannot be null. (Parameter 'request')";

    /// <summary>What the endpoint says when it grafts the route id onto the command it was handed.</summary>
    private const string HandlerDereferenced = "Object reference not set to an instance of an object.";

    /// <summary>
    /// A write with no body is a server error, and which of the two details it carries says how the route is
    /// written: an endpoint that does <c>command.Id = id</c> before sending dereferences the null itself,
    /// and one that sends the command as it came gets MediatR's <c>ArgumentNullException</c>.
    /// </summary>
    /// <remarks>
    /// Turns red when a missing body is a 400 — the answer this ought to be. The ids need not exist,
    /// because the graft happens in the endpoint, before the handler looks anything up. Issue 63.
    /// </remarks>
    [Theory]
    [InlineData("POST", "api/views", MediatorRefused)]
    [InlineData("POST", "api/permissions", MediatorRefused)]
    [InlineData("POST", "api/team-permissions", MediatorRefused)]
    [InlineData("POST", "api/roles", MediatorRefused)]
    [InlineData("POST", "api/team-roles", MediatorRefused)]
    [InlineData("POST", "api/users", MediatorRefused)]
    [InlineData("POST", "api/webhooks/subscribe", MediatorRefused)]
    [InlineData("POST", "api/application-templates", MediatorRefused)]
    [InlineData("PUT", $"api/views/{Bindable}", HandlerDereferenced)]
    [InlineData("PUT", $"api/teams/{Bindable}", HandlerDereferenced)]
    [InlineData("PUT", $"api/roles/{Bindable}", HandlerDereferenced)]
    [InlineData("PUT", $"api/users/{Bindable}", HandlerDereferenced)]
    [InlineData("POST", $"api/views/{Bindable}/teams", HandlerDereferenced)]
    [InlineData("POST", $"api/views/{Bindable}/notifications", HandlerDereferenced)]
    public async Task A_write_with_no_body_is_a_server_error(string method, string route, string detail)
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError, await Send(method, route, null));

        Assert.Equal(detail, problem.Detail);
    }

    /// <summary>
    /// A body of <c>null</c> is the same as no body: the deserializer produces a null command and nothing
    /// between it and the handler objects.
    /// </summary>
    [Fact]
    public async Task A_literal_null_body_is_the_same_server_error_as_no_body()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError, await Send("POST", "api/views", "null"));

        Assert.Equal(MediatorRefused, problem.Detail);
    }

    /// <summary>
    /// JSON the command cannot be built from is a bare 400 — malformed, the wrong JSON type, a member of the
    /// wrong type, and an enum name that is not defined all land here.
    /// </summary>
    /// <remarks>
    /// The empty body is the assertion worth reading: the deserializer's message names the member and the
    /// position, and none of it reaches the caller. Same shape as
    /// <see cref="MalformedRouteParameterTests.A_route_parameter_that_will_not_bind_is_a_bad_request_with_no_body"/>.
    /// </remarks>
    [Theory]
    [InlineData("{")]
    [InlineData("123")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{\"name\":42}")]
    [InlineData("{\"name\":\"X\",\"isTemplate\":\"maybe\"}")]
    [InlineData("{\"name\":\"X\",\"status\":\"Nonsense\"}")]
    public async Task A_body_the_command_cannot_be_built_from_is_a_bad_request_with_no_body(string body)
    {
        var response = await Send("POST", "api/views", body);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// A content type the route cannot read is a 415 rather than a 400, decided by the framework before the
    /// bytes are looked at — so well-formed JSON sent as <c>text/plain</c>, or with the header omitted, is
    /// refused for the header alone.
    /// </summary>
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData(null)]
    public async Task A_body_the_route_cannot_read_is_an_unsupported_media_type(string contentType)
    {
        var response = await Send("POST", "api/views", "{\"name\":\"X\"}", contentType);

        await AssertStatus(HttpStatusCode.UnsupportedMediaType, response);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// An empty object is accepted and creates a nameless view: nothing in the command declares a member
    /// required, so every member defaults and the row is written.
    /// </summary>
    /// <remarks>
    /// Turns red when the create command validates its own members. The stored name is asserted rather than
    /// the response's, because a null that only the mapping dropped would read the same on the way out.
    /// Issue 63 covers this as the other half of an unvalidated body.
    /// </remarks>
    [Fact]
    public async Task An_empty_object_creates_a_view_with_no_name()
    {
        await AssertStatus(HttpStatusCode.Created, await Send("POST", "api/views", "{}"));

        await using var db = NewContext();
        Assert.Null((await db.Views.SingleAsync(Ct)).Name);
    }

    /// <summary>
    /// The one write that binds a form rather than a JSON body is also the one that answers a missing body
    /// correctly — form binding has no nullable-oblivious parameter to make optional.
    /// </summary>
    [Fact]
    public async Task The_import_route_answers_a_missing_body_with_a_bad_request()
    {
        var response = await Send("POST", "api/views/actions/import", null);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// Authentication runs before binding, so a caller with no identity is refused whatever it sent.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_request_with_no_body_is_unauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/views");

        await AssertStatus(HttpStatusCode.Unauthorized, await Client().SendAsync(request, Ct));
    }

    private Task<HttpResponseMessage> Send(
        string method,
        string route,
        string body,
        string contentType = "application/json")
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType =
                contentType == null ? null : new MediaTypeHeaderValue(contentType);
        }

        return RootClient.SendAsync(request, Ct);
    }
}
