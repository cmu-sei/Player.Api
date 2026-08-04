// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Http;
using Player.Api.Features.Applications;
using Player.Api.Options;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// The endpoint filter that substitutes <c>Applications:UrlVariables</c> into an application instance's
/// <c>Url</c> and <c>Icon</c> on the way out.
/// </summary>
/// <remarks>
/// It is attached to the whole route group (<c>Startup.cs:367</c>) rather than to specific endpoints, so it
/// sees every response the API returns and has to leave alone anything that is not an application instance.
/// The variables let a deployment point applications at its own hostnames without editing view data — which
/// means a substitution that silently does not happen ships a broken link to the client.
/// </remarks>
public class ApplicationInstanceFilterTests
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["vm-ui"] = "vm.example.test",
        ["console-ui"] = "console.example.test",
    };

    [Fact]
    public async Task A_single_instance_has_its_url_and_icon_rewritten()
    {
        var app = new ApplicationInstance
        {
            Url = "https://{vm-ui}/views/1/vms",
            Icon = "https://{vm-ui}/assets/vm.png",
        };

        await Invoke(Variables, TypedResults.Ok(app));

        Assert.Equal("https://vm.example.test/views/1/vms", app.Url);
        Assert.Equal("https://vm.example.test/assets/vm.png", app.Icon);
    }

    /// <summary>
    /// Every variable is applied to every string, and more than once if the placeholder repeats — the
    /// substitution is a plain replace over the whole value, not a single lookup.
    /// </summary>
    [Fact]
    public async Task All_variables_are_applied_wherever_they_appear()
    {
        var app = new ApplicationInstance
        {
            Url = "https://{console-ui}/?vm=https://{vm-ui}/&fallback=https://{vm-ui}/",
        };

        await Invoke(Variables, TypedResults.Ok(app));

        Assert.Equal(
            "https://console.example.test/?vm=https://vm.example.test/&fallback=https://vm.example.test/",
            app.Url);
    }

    /// <summary>
    /// A list result is the common case — a team's applications come back as a collection, and each item has
    /// to be rewritten, not just the first.
    /// </summary>
    [Fact]
    public async Task Every_instance_in_a_collection_is_rewritten()
    {
        ApplicationInstance[] apps =
        [
            new() { Url = "https://{vm-ui}/one" },
            new() { Url = "https://{console-ui}/two" },
        ];

        await Invoke(Variables, TypedResults.Ok<IEnumerable<ApplicationInstance>>(apps));

        Assert.Equal("https://vm.example.test/one", apps[0].Url);
        Assert.Equal("https://console.example.test/two", apps[1].Url);
    }

    /// <summary>
    /// A placeholder with no matching variable is left as written rather than blanked, so a missing setting
    /// shows up as an obviously wrong url instead of a plausible one.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_placeholder_is_left_in_place()
    {
        var app = new ApplicationInstance { Url = "https://{missing-ui}/one" };

        await Invoke(Variables, TypedResults.Ok(app));

        Assert.Equal("https://{missing-ui}/one", app.Url);
    }

    /// <summary>
    /// Both properties are optional on an application, and a null one must not become the string "" or throw
    /// on the way through the filter.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_empty_url_or_icon_passes_through_unchanged(string value)
    {
        var app = new ApplicationInstance { Url = value, Icon = value };

        await Invoke(Variables, TypedResults.Ok(app));

        Assert.Equal(value, app.Url);
        Assert.Equal(value, app.Icon);
    }

    /// <summary>
    /// The default configuration defines no variables, which is the path most deployments take — the filter
    /// returns before touching the result at all.
    /// </summary>
    [Fact]
    public async Task With_no_variables_configured_nothing_is_rewritten()
    {
        var app = new ApplicationInstance { Url = "https://{vm-ui}/one" };

        await Invoke([], TypedResults.Ok(app));

        Assert.Equal("https://{vm-ui}/one", app.Url);
    }

    /// <summary>
    /// The filter runs on every endpoint in the group, so results of other shapes — and the null a
    /// <c>NoContent</c>-style endpoint hands back — have to fall through untouched.
    /// </summary>
    [Fact]
    public async Task A_result_that_is_not_an_application_instance_is_returned_as_is()
    {
        var result = TypedResults.Ok("https://{vm-ui}/one");

        Assert.Same(result, await Invoke(Variables, result));
        Assert.Equal("https://{vm-ui}/one", result.Value);
    }

    [Fact]
    public async Task A_null_result_is_returned_as_is()
    {
        Assert.Null(await Invoke(Variables, null));
    }

    /// <summary>
    /// A typed result whose value is null matches the same case as one carrying an instance, so the filter's
    /// null guard is what keeps it from dereferencing it.
    /// </summary>
    [Fact]
    public async Task A_result_carrying_a_null_instance_is_returned_as_is()
    {
        var result = TypedResults.Ok<ApplicationInstance>(null);

        Assert.Same(result, await Invoke(Variables, result));
    }

    /// <summary>Runs the filter over a result, as the route group does after the endpoint returns.</summary>
    private static async ValueTask<object> Invoke(Dictionary<string, string> variables, object result)
    {
        var filter = new ApplicationInstanceFilter(new AppOptions { UrlVariables = variables });

        return await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(new DefaultHttpContext()),
            _ => ValueTask.FromResult(result));
    }
}
