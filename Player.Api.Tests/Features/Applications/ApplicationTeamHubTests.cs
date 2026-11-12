// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Player.Api.Features.Applications.EventHandlers;
using Player.Api.Hubs;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// Covers the team hub message that keeps an open Player view's application list current: every change to
/// what a team's list shows tells that team's group, and only that team's group, over the real routes and
/// the real entity events.
/// </summary>
public class ApplicationTeamHubTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Application instances ------------------------------------------------------------------

    /// <summary>
    /// The message carries the team id and nothing about the application: clients fetch the list again
    /// through the endpoint that checks their own permissions.
    /// </summary>
    [Fact]
    public async Task Assigning_an_application_tells_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Assigned");
        var other = TestData.Team(view.Id, "Other");
        var application = TestData.Application(view.Id);
        await Seed(view, team, other, application);

        await AssertStatus(HttpStatusCode.Created, await RootClient.PostAsJsonAsync(
            $"api/teams/{team.Id}/application-instances", new { applicationId = application.Id }, Ct));

        var broadcast = Assert.Single(ApplicationsChanged(team.Id));
        Assert.Equal(team.Id, Assert.IsType<Guid>(broadcast.Argument));
        Assert.Empty(ApplicationsChanged(other.Id));
    }

    [Fact]
    public async Task Editing_an_assignment_tells_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var replacement = TestData.Application(view.Id, "Replacement");
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, replacement, instance);

        await AssertStatus(HttpStatusCode.OK, await RootClient.PutAsJsonAsync(
            $"api/application-instances/{instance.Id}", new { applicationId = replacement.Id, displayOrder = 1 }, Ct));

        Assert.NotEmpty(ApplicationsChanged(team.Id));
    }

    [Fact]
    public async Task Unassigning_an_application_tells_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        await AssertStatus(HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/application-instances/{instance.Id}", Ct));

        Assert.Single(ApplicationsChanged(team.Id));
    }

    /// <summary>A move renumbers the team's instances, so it is the team's list alone that changed.</summary>
    [Fact]
    public async Task Reordering_tells_only_the_team_whose_list_moved()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Reordered");
        var other = TestData.Team(view.Id, "Other");
        var first = TestData.Application(view.Id, "First");
        var second = TestData.Application(view.Id, "Second");
        var moved = TestData.ApplicationInstance(team.Id, second.Id, 1);
        await Seed(view, team, other, first, second,
            TestData.ApplicationInstance(team.Id, first.Id, 0), moved,
            TestData.ApplicationInstance(other.Id, first.Id, 0));

        await AssertStatus(HttpStatusCode.OK,
            await RootClient.PostAsync($"api/application-instances/{moved.Id}/move-up", null, Ct));

        Assert.NotEmpty(ApplicationsChanged(team.Id));
        Assert.Empty(ApplicationsChanged(other.Id));
    }

    // ---- Applications -----------------------------------------------------------------------------

    /// <summary>
    /// A rename shows on every team the application is assigned to, and on no team in the view that does not
    /// have it.
    /// </summary>
    [Fact]
    public async Task Editing_an_application_tells_every_team_showing_it()
    {
        var view = TestData.View();
        var red = TestData.Team(view.Id, "Red");
        var blue = TestData.Team(view.Id, "Blue");
        var white = TestData.Team(view.Id, "White");
        var application = TestData.Application(view.Id, "Before");
        await Seed(view, red, blue, white, application,
            TestData.ApplicationInstance(red.Id, application.Id),
            TestData.ApplicationInstance(blue.Id, application.Id));

        await AssertStatus(HttpStatusCode.OK, await RootClient.PutAsJsonAsync(
            $"api/applications/{application.Id}", new { name = "After", url = "https://example.test/after" }, Ct));

        Assert.Single(ApplicationsChanged(red.Id));
        Assert.Single(ApplicationsChanged(blue.Id));
        Assert.Empty(ApplicationsChanged(white.Id));
    }

    /// <summary>
    /// The instances go with the application, and their deletion is what tells the teams: by the time the
    /// application's own event is raised nothing records which teams had it.
    /// </summary>
    [Fact]
    public async Task Deleting_an_application_tells_every_team_that_showed_it()
    {
        var view = TestData.View();
        var red = TestData.Team(view.Id, "Red");
        var blue = TestData.Team(view.Id, "Blue");
        var white = TestData.Team(view.Id, "White");
        var application = TestData.Application(view.Id);
        await Seed(view, red, blue, white, application,
            TestData.ApplicationInstance(red.Id, application.Id),
            TestData.ApplicationInstance(blue.Id, application.Id));

        await AssertStatus(HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/applications/{application.Id}", Ct));

        Assert.Single(ApplicationsChanged(red.Id));
        Assert.Single(ApplicationsChanged(blue.Id));
        Assert.Empty(ApplicationsChanged(white.Id));
    }

    // ---- Application templates ------------------------------------------------------------------

    /// <summary>
    /// Templates are shared across views, so an edit reaches teams in each view with an application built from
    /// it, and none showing only applications built from another template.
    /// </summary>
    [Fact]
    public async Task Editing_a_template_tells_every_team_showing_an_application_built_from_it()
    {
        var template = TestData.ApplicationTemplate("Edited Template");
        var otherTemplate = TestData.ApplicationTemplate("Untouched Template");
        var first = TestData.View("First");
        var second = TestData.View("Second");
        var firstTeam = TestData.Team(first.Id);
        var secondTeam = TestData.Team(second.Id);
        var untouchedTeam = TestData.Team(second.Id, "Untouched");
        var firstApp = TestData.Application(first.Id, templateId: template.Id);
        var secondApp = TestData.Application(second.Id, templateId: template.Id);
        var untouchedApp = TestData.Application(second.Id, templateId: otherTemplate.Id);
        await Seed(template, otherTemplate, first, second, firstTeam, secondTeam, untouchedTeam,
            firstApp, secondApp, untouchedApp,
            TestData.ApplicationInstance(firstTeam.Id, firstApp.Id),
            TestData.ApplicationInstance(secondTeam.Id, secondApp.Id),
            TestData.ApplicationInstance(untouchedTeam.Id, untouchedApp.Id));

        await AssertStatus(HttpStatusCode.OK, await RootClient.PutAsJsonAsync(
            $"api/application-templates/{template.Id}",
            new { name = "Edited Template", url = "https://example.test/moved" }, Ct));

        Assert.Single(ApplicationsChanged(firstTeam.Id));
        Assert.Single(ApplicationsChanged(secondTeam.Id));
        Assert.Empty(ApplicationsChanged(untouchedTeam.Id));
    }

    /// <summary>
    /// Deleting an in-use template moves its applications to the replacement, which can change what they
    /// show; the move is an application edit, so the teams showing them are told.
    /// </summary>
    [Fact]
    public async Task Deleting_a_template_tells_the_teams_whose_applications_moved()
    {
        var template = TestData.ApplicationTemplate("Retired Template");
        var replacement = TestData.ApplicationTemplate("Replacement Template");
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id, templateId: template.Id);
        await Seed(template, replacement, view, team, application,
            TestData.ApplicationInstance(team.Id, application.Id));

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.DeleteAsync(
            $"api/application-templates/{template.Id}?replacementTemplateId={replacement.Id}", Ct));

        Assert.Single(ApplicationsChanged(team.Id));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// The application list messages sent to a team's group; the group also carries the team's
    /// notifications, which are not this suite's concern.
    /// </summary>
    private IReadOnlyList<HubBroadcast> ApplicationsChanged(Guid teamId) =>
        [.. Factory.Hub<TeamHub>().ToGroup(teamId)
            .Where(x => x.Method == ApplicationsBaseTeamHubHandler.ApplicationsChanged)];
}
