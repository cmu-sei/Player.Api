// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// Covers the <c>Applications</c> feature over HTTP — applications and their per-team instances on the
/// real routes, through the real authorization stack. Templates and the archive round trip are in
/// <see cref="ApplicationTemplateRequestTests"/>.
/// </summary>
public class ApplicationRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create / Edit / Delete -----------------------------------------------------------------

    /// <summary>
    /// Creating answers 201 with the application and a <c>Location</c> pointing at its own get route,
    /// which is the only thing telling a client where the new resource lives.
    /// </summary>
    [Fact]
    public async Task Create_persists_the_application()
    {
        var view = TestData.View();
        await Seed(view);

        var response = await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/applications",
            new { name = "Console", url = "https://example.test/console" },
            Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<Application>(response);

        Assert.Equal("Console", created.Name);
        Assert.Equal(view.Id, created.ViewId);
        Assert.Equal($"/api/applications/{created.Id}", response.Headers.Location?.AbsolutePath);

        // The stored row rather than the response, since the response is mapped from the entity the
        // handler holds in memory and would read the same whether or not the save took the values with it.
        await using var db = NewContext();
        var stored = await db.Applications.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Console", stored.Name);
        Assert.Equal("https://example.test/console", stored.Url);
        Assert.Equal(view.Id, stored.ViewId);
    }

    /// <summary>
    /// The template id survives the round trip, which is what lets an instance of this application fall
    /// back to the template for the properties it leaves unset.
    /// </summary>
    [Fact]
    public async Task Create_records_the_template_it_was_built_from()
    {
        var view = TestData.View();
        var template = TestData.ApplicationTemplate();
        await Seed(view, template);

        var created = await ReadAsync<Application>(await RootClient.PostAsJsonAsync(
            $"api/views/{view.Id}/applications",
            new { applicationTemplateId = template.Id },
            Ct));

        Assert.Equal(template.Id, created.ApplicationTemplateId);
    }

    [Fact]
    public async Task Create_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PostAsJsonAsync(
            $"api/views/{Guid.NewGuid()}/applications", new { name = "Orphan" }, Ct));
    }

    /// <summary>
    /// The team's role grants nothing, so <c>ManageView</c> on the view is the only permission in play —
    /// a view administrator adds applications to their own view without <c>ManageApplications</c>.
    /// </summary>
    [Fact]
    public async Task Create_is_allowed_for_a_caller_holding_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        var created = await ReadAsync<Application>(await Client(actor).PostAsJsonAsync(
            $"api/views/{view.Id}/applications", new { name = "Theirs" }, Ct));

        Assert.Equal("Theirs", created.Name);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        await Seed(view);

        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            $"api/views/{view.Id}/applications", new { name = "Nope" }, Ct));
    }

    /// <summary>
    /// The route carries only the id, so the body has to name the <c>viewId</c> as well: an edit replaces
    /// the whole resource, including which view it belongs to.
    /// </summary>
    [Fact]
    public async Task Edit_updates_the_application()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id, "Before");
        await Seed(view, application);

        var edited = await ReadAsync<Application>(await RootClient.PutAsJsonAsync(
            $"api/applications/{application.Id}",
            new { viewId = view.Id, name = "After", url = "https://example.test/after" },
            Ct));

        Assert.Equal("After", edited.Name);
        Assert.Equal("https://example.test/after", edited.Url);

        await using var db = NewContext();
        Assert.Equal("After", (await db.Applications.SingleAsync(x => x.Id == application.Id, Ct)).Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_application_as_not_found()
    {
        var view = TestData.View();
        await Seed(view);

        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/applications/{Guid.NewGuid()}", new { viewId = view.Id, name = "Ghost" }, Ct));
    }

    /// <summary>
    /// Authorized against the view named in the body, not the one the application is in — the route
    /// carries only the id.
    /// </summary>
    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync(
            $"api/applications/{application.Id}", new { viewId = view.Id, name = "Nope" }, Ct));
    }

    [Fact]
    public async Task Delete_removes_the_application()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/applications/{application.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Applications.AnyAsync(Ct));
    }

    /// <summary>
    /// The instances go with it, since an instance is only a placement of an application on a team.
    /// </summary>
    [Fact]
    public async Task Delete_removes_the_instances_of_the_application()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        await Seed(view, team, application, TestData.ApplicationInstance(team.Id, application.Id));

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/applications/{application.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.ApplicationInstances.AnyAsync(Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_application_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/applications/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/applications/{application.Id}", Ct));
    }

    // ---- Get / GetByView ------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_application()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id, "Findable");
        await Seed(view, application);

        var got = await ReadAsync<Application>(
            await RootClient.GetAsync($"api/applications/{application.Id}", Ct));

        Assert.Equal("Findable", got.Name);
    }

    /// <summary>
    /// Characterizes a defect: an id that does not exist is answered 200 with an empty body and no
    /// <c>Content-Type</c>, because the handler returns null and <c>TypedResults.Ok</c> writes nothing
    /// for a null value. It should be a 404 — a client cannot tell success from absence.
    /// </summary>
    /// <remarks>Turns red as soon as the handler reports a missing application as anything else.</remarks>
    [Fact]
    public async Task Get_returns_nothing_for_a_missing_application()
    {
        var response = await RootClient.GetAsync($"api/applications/{Guid.NewGuid()}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    /// <summary>
    /// The team's role grants nothing, so <c>ViewView</c> on the view is what reaches the application: it
    /// is authorized through its view, not through a team.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var application = TestData.Application(view.Id);
        await Seed(view, role, team, application);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ViewView]).SeedAsync();

        var got = await ReadAsync<Application>(
            await Client(actor).GetAsync($"api/applications/{application.Id}", Ct));

        Assert.Equal(application.Id, got.Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/applications/{application.Id}", Ct));
    }

    [Fact]
    public async Task GetByView_returns_only_that_view_applications()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        await Seed(
            view, otherView,
            TestData.Application(view.Id, "Mine"),
            TestData.Application(otherView.Id, "Theirs"));

        var applications = await ReadAsync<Application[]>(
            await RootClient.GetAsync($"api/views/{view.Id}/applications", Ct));

        Assert.Equal("Mine", Assert.Single(applications).Name);
    }

    [Fact]
    public async Task GetByView_reports_a_missing_view_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/views/{Guid.NewGuid()}/applications", Ct));
    }

    [Fact]
    public async Task GetByView_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        await Seed(view);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/views/{view.Id}/applications", Ct));
    }

    // ---- Instances: create / edit / delete ------------------------------------------------------

    /// <summary>
    /// The team is a route value and the application a body member; the answer is 201 with a
    /// <c>Location</c> on the instance's own route, not the team's.
    /// </summary>
    [Fact]
    public async Task CreateApplicationInstance_places_the_application_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id, "Console");
        await Seed(view, team, application);

        var response = await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances",
            new { applicationId = application.Id, displayOrder = 3 },
            Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<ApplicationInstance>(response);

        Assert.Equal(application.Id, created.ApplicationId);
        Assert.Equal(3, created.DisplayOrder);
        Assert.Equal("Console", created.Name);
        Assert.Equal($"/api/application-instances/{created.Id}", response.Headers.Location?.AbsolutePath);

        // The stored row rather than the response, since the response is mapped from the entity the
        // handler holds in memory and would read the same whether or not the save took the values with it.
        // TeamId is the "places it on the team" in the name, and the response model does not carry it.
        await using var db = NewContext();
        var stored = await db.ApplicationInstances.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal(team.Id, stored.TeamId);
        Assert.Equal(application.Id, stored.ApplicationId);
        Assert.Equal(3, stored.DisplayOrder);
    }

    /// <summary>
    /// The instance's display properties come from the application, falling back to its template for
    /// each one the application leaves unset.
    /// </summary>
    [Fact]
    public async Task CreateApplicationInstance_falls_back_to_the_template_for_unset_properties()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var template = TestData.ApplicationTemplate(
            "Template Name", "https://example.test/template", embeddable: true);
        var application = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            ViewId = view.Id,
            ApplicationTemplateId = template.Id
        };
        await Seed(view, team, template, application);

        var created = await ReadAsync<ApplicationInstance>(await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances",
            new { applicationId = application.Id },
            Ct));

        Assert.Equal("Template Name", created.Name);
        Assert.Equal("https://example.test/template", created.Url);
        Assert.Equal(template.Icon, created.Icon);
        Assert.True(created.Embeddable);
    }

    /// <summary>
    /// The team and view placeholders are substituted when the instance is read, which is how one
    /// application serves every team in a view.
    /// </summary>
    /// <remarks>
    /// Compared ignoring case: the substitution is a <c>Guid.ToString()</c> inside a projected
    /// expression (<c>Applications/MappingProfile.cs:48-51</c>), so the database performs it — and
    /// SQLite renders a <c>Guid</c> as upper-case text where PostgreSQL renders it lower-case. The
    /// casing is the provider's, not the application's.
    /// </remarks>
    [Fact]
    public async Task CreateApplicationInstance_substitutes_the_team_and_view_placeholders()
    {
        var view = TestData.View("My View");
        var team = TestData.Team(view.Id, "Blue Team");
        var application = TestData.Application(
            view.Id,
            "{teamName} console",
            "https://example.test/{viewId}/{teamId}?view={viewName}&team={teamName}");
        await Seed(view, team, application);

        var created = await ReadAsync<ApplicationInstance>(await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances",
            new { applicationId = application.Id },
            Ct));

        Assert.Equal("Blue Team console", created.Name);
        Assert.Equal(
            $"https://example.test/{view.Id}/{team.Id}?view=My%20View&team=Blue%20Team",
            created.Url,
            ignoreCase: true);
    }

    /// <summary>
    /// An instance is a placement within one view, so a cross-view pairing is refused with a 409 naming
    /// the rule.
    /// </summary>
    [Fact]
    public async Task CreateApplicationInstance_refuses_a_team_and_application_in_different_views()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id);
        var application = TestData.Application(otherView.Id);
        await Seed(view, otherView, team, application);

        var problem = await AssertProblem(HttpStatusCode.Conflict, await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances",
            new { applicationId = application.Id },
            Ct));

        Assert.Equal("The Team and Application must belong to the same View.", problem.Title);
    }

    [Fact]
    public async Task CreateApplicationInstance_reports_a_missing_team_as_not_found()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PostAsJsonAsync(
            $"api/teams/{Guid.NewGuid()}/application-instances",
            new { applicationId = application.Id },
            Ct));
    }

    [Fact]
    public async Task CreateApplicationInstance_reports_a_missing_application_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances",
            new { applicationId = Guid.NewGuid() },
            Ct));
    }

    [Fact]
    public async Task CreateApplicationInstance_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        await Seed(view, team, application);

        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances",
            new { applicationId = application.Id },
            Ct));
    }

    /// <summary>
    /// The route names only the instance, so the body carries <c>teamId</c> alongside the new
    /// <c>applicationId</c>; the instance keeps its id and takes the new application's display properties.
    /// </summary>
    [Fact]
    public async Task EditApplicationInstance_moves_it_to_another_application()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var first = TestData.Application(view.Id, "First");
        var second = TestData.Application(view.Id, "Second");
        var instance = TestData.ApplicationInstance(team.Id, first.Id);
        await Seed(view, team, first, second, instance);

        var edited = await ReadAsync<ApplicationInstance>(await RootClient.PutAsJsonAsync(
            $"api/application-instances/{instance.Id}",
            new { teamId = team.Id, applicationId = second.Id, displayOrder = 7 },
            Ct));

        Assert.Equal(second.Id, edited.ApplicationId);
        Assert.Equal("Second", edited.Name);
        Assert.Equal(7, edited.DisplayOrder);
    }

    [Fact]
    public async Task EditApplicationInstance_reports_a_missing_instance_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        await Seed(view, team, application);

        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/application-instances/{Guid.NewGuid()}",
            new { teamId = team.Id, applicationId = application.Id },
            Ct));
    }

    /// <summary>
    /// The same one-view rule as create: an edit cannot walk an instance across views.
    /// </summary>
    [Fact]
    public async Task EditApplicationInstance_refuses_a_team_and_application_in_different_views()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var elsewhere = TestData.Application(otherView.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, otherView, team, application, elsewhere, instance);

        var problem = await AssertProblem(HttpStatusCode.Conflict, await RootClient.PutAsJsonAsync(
            $"api/application-instances/{instance.Id}",
            new { teamId = team.Id, applicationId = elsewhere.Id },
            Ct));

        Assert.Equal("The Team and Application must belong to the same View.", problem.Title);
    }

    [Fact]
    public async Task DeleteApplicationInstance_removes_the_instance_and_leaves_the_application()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/application-instances/{instance.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.ApplicationInstances.AnyAsync(Ct));
        Assert.True(await db.Applications.AnyAsync(x => x.Id == application.Id, Ct));
    }

    [Fact]
    public async Task DeleteApplicationInstance_reports_a_missing_instance_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/application-instances/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task DeleteApplicationInstance_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/application-instances/{instance.Id}", Ct));
    }

    // ---- Instances: read ------------------------------------------------------------------------

    [Fact]
    public async Task GetApplicationInstance_returns_the_instance()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id, "Findable");
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        var result = await ReadAsync<ApplicationInstance>(
            await RootClient.GetAsync($"api/application-instances/{instance.Id}", Ct));

        Assert.Equal("Findable", result.Name);
        Assert.Equal(view.Id, result.ViewId);
    }

    /// <summary>
    /// Characterizes the same defect as <see cref="Get_returns_nothing_for_a_missing_application"/>: a
    /// missing instance is a 200 with an empty body instead of a 404.
    /// </summary>
    /// <remarks>Turns red as soon as the handler reports a missing instance as anything else.</remarks>
    [Fact]
    public async Task GetApplicationInstance_returns_nothing_for_a_missing_instance()
    {
        var response = await RootClient.GetAsync($"api/application-instances/{Guid.NewGuid()}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    /// <summary>
    /// The team's role grants nothing, so <c>ViewTeam</c> on the instance's own team is the only
    /// permission in play — a team can read what has been placed on it.
    /// </summary>
    [Fact]
    public async Task GetApplicationInstance_is_allowed_for_a_caller_holding_ViewTeam_on_the_team()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, role, team, application, instance);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var got = await ReadAsync<ApplicationInstance>(
            await Client(actor).GetAsync($"api/application-instances/{instance.Id}", Ct));

        Assert.Equal(instance.Id, got.Id);
    }

    /// <summary>
    /// Both teams share a role that grants nothing, so <c>ViewTeam</c> on the other team is the caller's
    /// only permission — membership in the same view says nothing about this team's instances.
    /// </summary>
    [Fact]
    public async Task GetApplicationInstance_is_forbidden_for_a_caller_on_another_team()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var otherTeam = TestData.Team(view.Id, "Other", role.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, role, team, otherTeam, application, instance);

        var actor = await Actor()
            .OnTeam(otherTeam, teamPermissions: [TeamPermission.ViewTeam])
            .SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/application-instances/{instance.Id}", Ct));
    }

    [Fact]
    public async Task GetApplicationInstancesByTeam_returns_that_team_instances_in_display_order()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var otherTeam = TestData.Team(view.Id, "Other");
        var first = TestData.Application(view.Id, "First");
        var second = TestData.Application(view.Id, "Second");
        await Seed(
            view, team, otherTeam, first, second,
            TestData.ApplicationInstance(team.Id, second.Id, 2),
            TestData.ApplicationInstance(team.Id, first.Id, 1),
            TestData.ApplicationInstance(otherTeam.Id, first.Id));

        var instances = await ReadAsync<ApplicationInstance[]>(
            await RootClient.GetAsync($"api/teams/{team.Id}/application-instances", Ct));

        Assert.Equal(["First", "Second"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task GetApplicationInstancesByTeam_reports_a_missing_team_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/teams/{Guid.NewGuid()}/application-instances", Ct));
    }

    [Fact]
    public async Task GetApplicationInstancesByTeam_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/teams/{team.Id}/application-instances", Ct));
    }

    // ---- MoveApplicationInstance ----------------------------------------------------------------

    /// <summary>
    /// Moving up renumbers the whole team's instances by position, so the returned order is what the
    /// team will see next.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_moves_an_instance_up_one_place()
    {
        var ids = await SeedOrderedInstances("A", "B", "C");

        var instances = await ReadAsync<ApplicationInstance[]>(
            await Move(RootClient, ids["B"], "up"));

        Assert.Equal(["B", "A", "C"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task MoveApplicationInstance_moves_an_instance_down_one_place()
    {
        var ids = await SeedOrderedInstances("A", "B", "C");

        var instances = await ReadAsync<ApplicationInstance[]>(
            await Move(RootClient, ids["B"], "down"));

        Assert.Equal(["A", "C", "B"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task MoveApplicationInstance_leaves_the_first_instance_where_it_is()
    {
        var ids = await SeedOrderedInstances("A", "B", "C");

        var instances = await ReadAsync<ApplicationInstance[]>(
            await Move(RootClient, ids["A"], "up"));

        Assert.Equal(["A", "B", "C"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task MoveApplicationInstance_leaves_the_last_instance_where_it_is()
    {
        var ids = await SeedOrderedInstances("A", "B", "C");

        var instances = await ReadAsync<ApplicationInstance[]>(
            await Move(RootClient, ids["C"], "down"));

        Assert.Equal(["A", "B", "C"], instances.Select(x => x.Name));
    }

    /// <summary>
    /// Both instances start at the same display order, so a pass that renumbered every instance sharing a
    /// position — rather than only the moved instance's team — would show as a change on the other team.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_leaves_other_teams_alone()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var otherTeam = TestData.Team(view.Id, "Other");
        var application = TestData.Application(view.Id);
        var mine = TestData.ApplicationInstance(team.Id, application.Id, 5);
        var theirs = TestData.ApplicationInstance(otherTeam.Id, application.Id, 5);
        await Seed(view, team, otherTeam, application, mine, theirs);

        await AssertStatus(HttpStatusCode.OK, await Move(RootClient, mine.Id, "down"));

        await using var db = NewContext();
        Assert.Equal(5, (await db.ApplicationInstances.SingleAsync(x => x.Id == theirs.Id, Ct)).DisplayOrder);
    }

    [Fact]
    public async Task MoveApplicationInstance_reports_a_missing_instance_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await Move(RootClient, Guid.NewGuid(), "up"));
    }

    /// <summary>
    /// The team's role grants nothing, so <c>ManageTeam</c> on the instance's team carries the reorder on
    /// its own — reordering is a team-level change, not a view-level one.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_is_allowed_for_a_caller_holding_ManageTeam()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, role, team, application, instance);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ManageTeam]).SeedAsync();

        Assert.Single(await ReadAsync<ApplicationInstance[]>(
            await Move(Client(actor), instance.Id, "up")));
    }

    /// <summary>
    /// The team's role grants nothing, so <c>ViewTeam</c> is the caller's only permission: reading a team
    /// is not managing it.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_is_forbidden_for_a_caller_holding_only_ViewTeam()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, role, team, application, instance);

        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Move(Client(actor), instance.Id, "up"));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Reorders an instance. The direction is a route, not a body member — two endpoints over one
    /// handler — and neither takes a body at all.
    /// </summary>
    private static Task<HttpResponseMessage> Move(HttpClient client, Guid id, string direction)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PostAsync($"api/application-instances/{id}/move-{direction}", null, Ct);
    }

    /// <summary>
    /// Seeds one team with an instance per name, in the order given, and returns the instance ids by
    /// application name.
    /// </summary>
    private async Task<Dictionary<string, Guid>> SeedOrderedInstances(params string[] names)
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var ids = new Dictionary<string, Guid>();

        for (var i = 0; i < names.Length; i++)
        {
            var application = TestData.Application(view.Id, names[i]);
            var instance = TestData.ApplicationInstance(team.Id, application.Id, i);
            await Seed(application, instance);
            ids[names[i]] = instance.Id;
        }

        return ids;
    }
}
