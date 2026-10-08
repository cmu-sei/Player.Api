// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Player.Api.Data.Data.Models;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// The default authorization policy <c>Startup.ApplyPolicies</c> builds: an authenticated user holding every
/// scope in <c>Authorization:AuthorizationScope</c> (<c>player</c>). It applies to each family of mapped
/// route: the minimal-API route group, the MVC controllers' <c>[Authorize]</c>, the hubs and the Prometheus
/// scraping endpoint.
/// </summary>
/// <remarks>
/// Each caller is an actor holding the route's own permission, and <c>TestAuthHandler.ScopeHeader</c>
/// replaces the scopes its token carries for that one request. So a refusal is the scope requirement's alone,
/// and each denial's control request, the same request with the shipped scope, shows the actor is otherwise
/// admitted.
/// </remarks>
public class DefaultPolicyScopeTests(DatabaseFixture fixture, PlayerAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A scope another Crucible API's tokens carry, and Player's policy does not ask for.</summary>
    private const string OtherScope = "player-vm";

    /// <summary>The scope the shipped <c>Authorization:AuthorizationScope</c> requires.</summary>
    private const string PlayerScope = "player";

    [Fact]
    public async Task A_minimal_api_route_is_forbidden_for_a_token_without_the_player_scope_but_allowed_with_it()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewViews).SeedAsync();
        using var withoutScope = new HttpRequestMessage(HttpMethod.Get, "api/views");
        withoutScope.Headers.Add(TestAuthHandler.ScopeHeader, OtherScope);
        using var withScope = new HttpRequestMessage(HttpMethod.Get, "api/views");
        withScope.Headers.Add(TestAuthHandler.ScopeHeader, PlayerScope);

        var refused = await Client(actor).SendAsync(withoutScope, Ct);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        await AssertStatus(HttpStatusCode.OK, await Client(actor).SendAsync(withScope, Ct));
    }

    [Fact]
    public async Task A_controller_route_is_forbidden_for_a_token_without_the_player_scope_but_allowed_with_it()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewViews).SeedAsync();
        using var withoutScope = new HttpRequestMessage(HttpMethod.Get, "api/files");
        withoutScope.Headers.Add(TestAuthHandler.ScopeHeader, OtherScope);
        using var withScope = new HttpRequestMessage(HttpMethod.Get, "api/files");
        withScope.Headers.Add(TestAuthHandler.ScopeHeader, PlayerScope);

        var refused = await Client(actor).SendAsync(withoutScope, Ct);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        await AssertStatus(HttpStatusCode.OK, await Client(actor).SendAsync(withScope, Ct));
    }

    /// <summary>The xAPI controller carries its own <c>[Authorize]</c>, outside <c>BaseController</c>.</summary>
    [Fact]
    public async Task An_xapi_route_is_forbidden_for_a_token_without_the_player_scope_but_allowed_with_it()
    {
        var view = TestData.View();
        await Seed(view);
        var actor = await Actor().OnNewTeam(view.Id, viewPermissions: [ViewPermission.ViewView]).SeedAsync();
        using var withoutScope = new HttpRequestMessage(HttpMethod.Post, $"api/xapi/viewed/view/{view.Id}");
        withoutScope.Headers.Add(TestAuthHandler.ScopeHeader, OtherScope);
        using var withScope = new HttpRequestMessage(HttpMethod.Post, $"api/xapi/viewed/view/{view.Id}");
        withScope.Headers.Add(TestAuthHandler.ScopeHeader, PlayerScope);

        var refused = await Client(actor).SendAsync(withoutScope, Ct);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        await AssertStatus(HttpStatusCode.OK, await Client(actor).SendAsync(withScope, Ct));
    }

    [Fact]
    public async Task A_hub_negotiate_is_forbidden_for_a_token_without_the_player_scope_but_allowed_with_it()
    {
        var actor = await ViewMember();
        using var withoutScope = new HttpRequestMessage(HttpMethod.Post, "/hubs/view/negotiate?negotiateVersion=1");
        withoutScope.Headers.Add(TestAuthHandler.ScopeHeader, OtherScope);
        using var withScope = new HttpRequestMessage(HttpMethod.Post, "/hubs/view/negotiate?negotiateVersion=1");
        withScope.Headers.Add(TestAuthHandler.ScopeHeader, PlayerScope);

        var refused = await Client(actor).SendAsync(withoutScope, Ct);

        await AssertStatus(HttpStatusCode.Forbidden, refused);
        await AssertStatus(HttpStatusCode.OK, await Client(actor).SendAsync(withScope, Ct));
    }

    /// <summary>The scraping endpoint asks for no permission, only the default policy.</summary>
    [Fact]
    public async Task Metrics_are_served_to_an_actor()
    {
        var actor = await ViewMember();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("/metrics", Ct));
    }

    [Fact]
    public async Task Metrics_are_forbidden_for_a_token_without_the_player_scope()
    {
        var actor = await ViewMember();
        using var withoutScope = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        withoutScope.Headers.Add(TestAuthHandler.ScopeHeader, OtherScope);

        await AssertStatus(HttpStatusCode.Forbidden, await Client(actor).SendAsync(withoutScope, Ct));
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task Metrics_without_an_identity_are_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("/metrics", Ct));
    }

    /// <summary>A member of a view of its own, holding no permission: the caller the policy alone decides for.</summary>
    private async Task<TestActor> ViewMember()
    {
        var view = TestData.View();
        await Seed(view);

        return await Actor().OnNewTeam(view.Id).SeedAsync();
    }
}
