// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Endpoints;

/// <summary>
/// Pins the http surface of all 90 <c>IEndpoint</c> classes: the verb, the pattern, the <c>WithName</c>
/// value, and the swagger text that rides along with them.
/// </summary>
/// <remarks>
/// Nothing else in the suite reaches route registration — <see cref="ApiTestHost"/> is a plain container
/// that gets to the MediatR handlers but never to the routes. The names below are what player.ui, alloy.ui
/// and the terraform provider generate their clients from, so a rename here is a breaking change for
/// consumers this repo cannot see. That is why the contract is asserted literally rather than sampled.
/// </remarks>
public class EndpointRegistrationTests(EndpointRouteTable table) : IClassFixture<EndpointRouteTable>
{
    /// <summary>
    /// Every route, keyed by the tag its feature namespace produces, as <c>VERB pattern -> name</c> in
    /// ordinal order. Split per feature so a failure names the area and prints a short diff.
    /// </summary>
    private static readonly Dictionary<string, string[]> Contract = new()
    {
        ["Application"] =
        [
            "DELETE /api/application-instances/{id} -> deleteApplicationInstance",
            "DELETE /api/application-templates/{id} -> deleteApplicationTemplate",
            "DELETE /api/applications/{id} -> deleteApplication",
            "GET /api/application-instances/{id} -> getApplicationInstance",
            "GET /api/application-templates -> getApplicationTemplates",
            "GET /api/application-templates/actions/export -> exportApplicationTemplates",
            "GET /api/application-templates/{id} -> getApplicationTemplate",
            "GET /api/applications/{id} -> getApplication",
            "GET /api/teams/{id}/application-instances -> getTeamApplicationInstances",
            "GET /api/views/{viewId}/applications -> getViewApplications",
            "POST /api/application-instances/{id}/move-down -> moveDownApplicationInstance",
            "POST /api/application-instances/{id}/move-up -> moveUpApplicationInstance",
            "POST /api/application-templates -> createApplicationTemplate",
            "POST /api/application-templates/actions/import -> importApplicationTemplates",
            "POST /api/teams/{id}/application-instances -> createApplicationInstance",
            "POST /api/views/{viewId}/applications -> createApplication",
            "PUT /api/application-instances/{id} -> updateApplicationInstance",
            "PUT /api/application-templates/{id} -> updateApplicationTemplate",
            "PUT /api/applications/{id} -> updateApplication",
        ],
        ["Permission"] =
        [
            "DELETE /api/permissions/{id} -> deletePermission",
            "DELETE /api/roles/{roleId}/permissions/{permissionId} -> removePermissionFromRole",
            "GET /api/permissions -> getPermissions",
            "GET /api/permissions/mine -> getMyPermissions",
            "GET /api/permissions/{id} -> getPermission",
            "POST /api/permissions -> createPermission",
            "POST /api/roles/{roleId}/permissions/{permissionId} -> addPermissionToRole",
            "PUT /api/permissions/{id} -> updatePermission",
        ],
        ["Role"] =
        [
            "DELETE /api/roles/{id} -> deleteRole",
            "GET /api/roles -> getRoles",
            "GET /api/roles/name/{name} -> getRoleByName",
            "GET /api/roles/{id} -> getRole",
            "POST /api/roles -> createRole",
            "PUT /api/roles/{id} -> updateRole",
        ],
        ["Team"] =
        [
            "DELETE /api/teams/{id} -> deleteTeam",
            "GET /api/me/views/{id}/teams -> getMyViewTeams",
            "GET /api/teams -> getTeams",
            "GET /api/teams/{id} -> getTeam",
            "GET /api/users/{userId}/views/{viewId}/teams -> getUserViewTeams",
            "GET /api/views/{id}/teams -> getViewTeams",
            "POST /api/teams/{id}/notifications -> broadcastToTeam",
            "POST /api/users/{userId}/teams/{teamId}/primary -> setUserPrimaryTeam",
            "POST /api/views/{id}/teams -> createTeam",
            "PUT /api/teams/{id} -> updateTeam",
        ],
        ["TeamMembership"] =
        [
            "GET /api/team-memberships/{id} -> getTeamMembership",
            "GET /api/users/{userId}/views/{viewId}/team-memberships -> getTeamMemberships",
            "PUT /api/team-memberships/{id} -> updateTeamMembership",
        ],
        ["TeamPermission"] =
        [
            "DELETE /api/team-permissions/{id} -> deleteTeamPermission",
            "DELETE /api/team-roles/{roleId}/permissions/{permissionId} -> removeTeamPermissionFromRole",
            "DELETE /api/teams/{teamId}/permissions/{permissionId} -> removeTeamPermissionFromTeam",
            "GET /api/team-permissions -> getTeamPermissions",
            "GET /api/team-permissions/mine -> getMyTeamPermissions",
            "GET /api/team-permissions/{id} -> getTeamPermission",
            "POST /api/team-permissions -> createTeamPermission",
            "POST /api/team-roles/{roleId}/permissions/{permissionId} -> addTeamPermissionToRole",
            "POST /api/teams/{teamId}/permissions/{permissionId} -> addTeamPermissionToTeam",
            "PUT /api/team-permissions/{id} -> updateTeamPermission",
        ],
        ["TeamPermissionScope"] =
        [
            "DELETE /api/teams/{teamId}/scopes/{targetTeamId} -> removeTeamPermissionScope",
            "POST /api/teams/{teamId}/scopes/{targetTeamId} -> addTeamPermissionScope",
        ],
        ["TeamRole"] =
        [
            "DELETE /api/team-roles/{id} -> deleteTeamRole",
            "GET /api/team-roles -> getTeamRoles",
            "GET /api/team-roles/{id} -> getTeamRole",
            "POST /api/team-roles -> createTeamRole",
            "PUT /api/team-roles/{id} -> updateTeamRole",
        ],
        ["User"] =
        [
            "DELETE /api/teams/{teamId}/users/{userId} -> removeUserFromTeam",
            "DELETE /api/users/{id} -> deleteUser",
            "GET /api/teams/{id}/users -> getTeamUsers",
            "GET /api/users -> getUsers",
            "GET /api/users/{id} -> getUser",
            "GET /api/views/{id}/users -> getViewUsers",
            "POST /api/teams/{teamId}/users/{userId} -> addUserToTeam",
            "POST /api/users -> createUser",
            "POST /api/views/{viewId}/users/{userId}/notifications -> broadcastToUser",
            "PUT /api/users/{id} -> updateUser",
        ],
        ["View"] =
        [
            "DELETE /api/views/{id} -> deleteView",
            "DELETE /api/views/{id}/notifications -> deleteViewNotifications",
            "DELETE /api/views/{id}/notifications/{key} -> deleteNotification",
            "GET /api/me/views -> getMyViews",
            "GET /api/users/{id}/views -> getUserViews",
            "GET /api/views -> getViews",
            "GET /api/views/actions/export -> exportViews",
            "GET /api/views/{id} -> getView",
            "GET /api/views/{id}/notifications -> getAllViewNotifications",
            "POST /api/views -> createView",
            "POST /api/views/actions/import -> importViews",
            "POST /api/views/{id}/clone -> cloneView",
            "POST /api/views/{id}/notifications -> broadcastToView",
            "PUT /api/views/{id} -> updateView",
        ],
        ["ViewMembership"] =
        [
            "GET /api/users/{userId}/view-memberships -> getViewMemberships",
            "GET /api/view-memberships/{id} -> getViewMembership",
        ],
        ["Webhook"] =
        [
            "DELETE /api/webhooks/{id} -> deleteWebhookSubscription",
            "GET /api/webhooks -> getAllWebhooks",
            "PATCH /api/webhooks/{id} -> partialUpdateWebhookSubscription",
            "POST /api/webhooks/subscribe -> createWebhookSubscription",
            "PUT /api/webhooks/{id} -> updateWebhookSubscription",
        ],
    };

    /// <summary>Route parameters are unconstrained, so their names are the whole contract for them.</summary>
    private static readonly string[] KnownRouteParameters =
        ["id", "key", "name", "permissionId", "roleId", "targetTeamId", "teamId", "userId", "viewId"];

    public static TheoryData<string> FeatureTags() => new(Contract.Keys);

    public static TheoryData<Type> EndpointTypes() => new(EndpointHarness.DiscoverEndpointTypes());

    // ---- The route table as a contract ---------------------------------------------------------------

    /// <summary>
    /// The verb, pattern and name of every route in one feature area. These triples are the generated
    /// client surface; changing one breaks consumers without failing anything else here.
    /// </summary>
    [Theory]
    [MemberData(nameof(FeatureTags))]
    public void Registered_routes_match_the_published_contract(string tag)
    {
        var registered = Describe(table.Routes.Where(x => TagOf(x) == tag));

        Assert.Equal(Contract[tag], registered);
    }

    /// <summary>
    /// Guards the per-area theory above: a new feature namespace, or an untagged route, would otherwise
    /// belong to no case and be asserted nowhere.
    /// </summary>
    [Fact]
    public void The_contract_accounts_for_every_registered_route()
    {
        string[] expected = [.. Contract.Keys.Order(StringComparer.Ordinal)];
        string[] tagged = [.. table.Routes.Select(TagOf).Distinct().Order(StringComparer.Ordinal)];

        Assert.Equal(expected, tagged);
        Assert.Equal(Contract.Values.Sum(x => x.Length), table.Routes.Length);
    }

    // ---- Registration invariants --------------------------------------------------------------------

    /// <summary>Catches an <c>Endpoint</c> that compiles but returns no builders, so registers nothing.</summary>
    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Every_endpoint_registers_at_least_one_route(Type endpointType)
    {
        using var harness = new EndpointHarness();

        Assert.NotEmpty(harness.Register(endpointType));
    }

    /// <summary>
    /// A duplicate <c>WithName</c> is an ambiguous-route failure at startup and a name clash in every
    /// generated client. Endpoints live one per file, so nothing else would notice a copied name.
    /// </summary>
    [Fact]
    public void Endpoint_names_are_unique_across_the_api()
    {
        string[] duplicated =
        [
            .. table.Routes
                .GroupBy(NameOf)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .Order(StringComparer.Ordinal)
        ];

        Assert.Empty(duplicated);
    }

    /// <summary>
    /// The name is what clients call the operation; the summary and description are the only prose swagger
    /// carries. All 94 routes supply all three today, and none should be added without them.
    /// </summary>
    [Fact]
    public void Every_route_carries_a_name_a_summary_and_a_description()
    {
        string[] unnamed = [.. table.Routes.Where(x => string.IsNullOrWhiteSpace(NameOf(x))).Select(PatternOf)];
        string[] unsummarised = [.. table.Routes.Where(x => string.IsNullOrWhiteSpace(SummaryOf(x))).Select(NameOf)];
        string[] undescribed = [.. table.Routes.Where(x => string.IsNullOrWhiteSpace(DescriptionOf(x))).Select(NameOf)];

        Assert.Empty(unnamed);
        Assert.Empty(unsummarised);
        Assert.Empty(undescribed);
    }

    /// <summary>Swagger groups by tag, so a route with none or with several lands in the wrong place.</summary>
    [Fact]
    public void Every_route_carries_exactly_one_feature_tag()
    {
        Assert.All(table.Routes, x => Assert.Single(TagsOf(x)));
    }

    /// <summary>
    /// <c>IEndpoint.GroupEndpoints</c> drops the group name's last character rather than depluralising it,
    /// so the tags only read correctly because every feature namespace happens to end in "s".
    /// </summary>
    [Fact]
    public void Grouping_takes_the_tag_by_dropping_the_last_character_of_the_group_name()
    {
        using var plural = new EndpointHarness();
        using var singular = new EndpointHarness();
        var endpoint = new Player.Api.Features.Webhooks.GetAll.Endpoint();

        Assert.Equal(["Webhook"], TagsOf(Assert.Single(plural.Register(endpoint, "Webhooks"))));
        Assert.Equal(["Webhoo"], TagsOf(Assert.Single(singular.Register(endpoint, "Webhook"))));
    }

    /// <summary>
    /// Route parameters are never constrained, defaulted or catch-all — an id is only validated once it
    /// binds — and their names come from a fixed vocabulary the generated clients mirror.
    /// </summary>
    [Fact]
    public void Route_parameters_come_from_a_known_vocabulary_and_carry_no_inline_constraints()
    {
        var parameters = table.Routes.SelectMany(x => x.RoutePattern.Parameters).ToArray();
        string[] names = [.. parameters.Select(x => x.Name).Distinct().Order(StringComparer.Ordinal)];

        Assert.Equal(KnownRouteParameters, names);
        Assert.All(parameters, x =>
        {
            Assert.Empty(x.ParameterPolicies);
            Assert.False(x.IsOptional);
            Assert.False(x.IsCatchAll);
        });
    }

    // ---- One endpoint in isolation -------------------------------------------------------------------

    /// <summary>
    /// A worked example of the single-endpoint path: one <c>Endpoint</c> mapping two routes onto the same
    /// pattern, which is the only place in the api a PUT and a PATCH share one.
    /// </summary>
    [Fact]
    public void Webhook_edit_registers_a_put_and_a_patch()
    {
        using var harness = new EndpointHarness();

        var routes = harness.Register(new Player.Api.Features.Webhooks.Edit.Endpoint());

        Assert.Equal(2, routes.Length);
        Assert.All(routes, x => Assert.Equal("/api/webhooks/{id}", x.RoutePattern.RawText));
        Assert.Equal(["PATCH", "PUT"], routes.SelectMany(VerbsOf).Order());
        Assert.Equal(
            ["partialUpdateWebhookSubscription", "updateWebhookSubscription"],
            routes.Select(NameOf).Order());
    }

    private static string[] Describe(IEnumerable<RouteEndpoint> routes) =>
        [.. routes
            .Select(x => $"{string.Join(",", VerbsOf(x).Order(StringComparer.Ordinal))} {PatternOf(x)} -> {NameOf(x)}")
            .Order(StringComparer.Ordinal)];

    private static string PatternOf(RouteEndpoint route) => route.RoutePattern.RawText;

    private static string NameOf(RouteEndpoint route) =>
        route.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName;

    private static string SummaryOf(RouteEndpoint route) =>
        route.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary;

    private static string DescriptionOf(RouteEndpoint route) =>
        route.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description;

    private static IEnumerable<string> VerbsOf(RouteEndpoint route) =>
        route.Metadata.GetMetadata<HttpMethodMetadata>().HttpMethods;

    private static string[] TagsOf(RouteEndpoint route)
    {
        var tags = route.Metadata.GetMetadata<ITagsMetadata>()?.Tags;

        return tags is null ? [] : [.. tags];
    }

    // Grouping must not throw on an untagged or over-tagged route; dedicated tests report those.
    private static string TagOf(RouteEndpoint route) => TagsOf(route) is [var tag] ? tag : null;
}
