// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// Covers the <c>Applications</c> feature's handlers for applications and their per-team instances.
/// Templates and the archive round trip are in <see cref="ApplicationTemplateRequestTests"/>.
/// </summary>
public class ApplicationRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_application()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await SendAsync(new Create.Command
        {
            ViewId = view.Id,
            Name = "Console",
            Url = "https://example.test/console"
        });

        Assert.Equal("Console", created.Name);
        Assert.Equal(view.Id, created.ViewId);

        await using var db = NewContext();
        Assert.True(await db.Applications.AnyAsync(x => x.Id == created.Id, Ct));
    }

    [Fact]
    public async Task Create_records_the_template_it_was_built_from()
    {
        var view = TestData.View();
        var template = TestData.ApplicationTemplate();
        await Seed(view, template);

        var created = await SendAsync(new Create.Command
        {
            ViewId = view.Id,
            ApplicationTemplateId = template.Id
        });

        Assert.Equal(template.Id, created.ApplicationTemplateId);
    }

    [Fact]
    public async Task Create_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Views.View>>(
            () => SendAsync(new Create.Command { ViewId = Guid.NewGuid(), Name = "Orphan" }));
    }

    /// <summary>
    /// Scoped to the view, so <c>ManageView</c> is enough — a view administrator adds applications to
    /// their own view without <c>ManageApplications</c>.
    /// </summary>
    [Fact]
    public async Task Create_is_allowed_for_a_caller_holding_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var created = await SendAsync(caller, new Create.Command { ViewId = view.Id, Name = "Theirs" });

        Assert.Equal("Theirs", created.Name);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Create.Command { ViewId = view.Id, Name = "Nope" }));
    }

    [Fact]
    public async Task Edit_updates_the_application()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id, "Before");
        await Seed(view, application);

        var edited = await SendAsync(new Edit.Command
        {
            Id = application.Id,
            ViewId = view.Id,
            Name = "After",
            Url = "https://example.test/after"
        });

        Assert.Equal("After", edited.Name);
        Assert.Equal("https://example.test/after", edited.Url);
    }

    [Fact]
    public async Task Edit_reports_a_missing_application_as_not_found()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<EntityNotFoundException<Application>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), ViewId = view.Id, Name = "Ghost" }));
    }

    /// <summary>
    /// Authorized against the view named in the request, not the one the application is in.
    /// </summary>
    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Edit.Command { Id = application.Id, ViewId = view.Id, Name = "Nope" }));
    }

    [Fact]
    public async Task Delete_removes_the_application()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await SendAsync(new Delete.Command { Id = application.Id });

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

        await SendAsync(new Delete.Command { Id = application.Id });

        await using var db = NewContext();
        Assert.False(await db.ApplicationInstances.AnyAsync(Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_application_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Application>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Delete.Command { Id = application.Id }));
    }

    // ---- Get / GetByView ------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_application()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id, "Findable");
        await Seed(view, application);

        Assert.Equal("Findable", (await SendAsync(new Get.Query { Id = application.Id })).Name);
    }

    /// <summary>
    /// Characterizes current behaviour: the handler returns null for an id that does not exist rather
    /// than throwing, unlike the feature's write handlers.
    /// </summary>
    [Fact]
    public async Task Get_returns_nothing_for_a_missing_application()
    {
        Assert.Null(await SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        await Seed(view, team, application);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        Assert.Equal(
            application.Id,
            (await SendAsync(caller, new Get.Query { Id = application.Id })).Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Get.Query { Id = application.Id }));
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

        var applications = await SendAsync(new GetByView.Query { ViewId = view.Id });

        Assert.Equal("Mine", Assert.Single(applications).Name);
    }

    [Fact]
    public async Task GetByView_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Views.View>>(
            () => SendAsync(new GetByView.Query { ViewId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetByView_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetByView.Query { ViewId = view.Id }));
    }

    // ---- Instances: create / edit / delete ------------------------------------------------------

    [Fact]
    public async Task CreateApplicationInstance_places_the_application_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id, "Console");
        await Seed(view, team, application);

        var created = await SendAsync(new CreateApplicationInstance.Command
        {
            TeamId = team.Id,
            ApplicationId = application.Id,
            DisplayOrder = 3
        });

        Assert.Equal(application.Id, created.ApplicationId);
        Assert.Equal(3, created.DisplayOrder);
        Assert.Equal("Console", created.Name);

        await using var db = NewContext();
        Assert.True(await db.ApplicationInstances.AnyAsync(x => x.Id == created.Id, Ct));
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

        var created = await SendAsync(new CreateApplicationInstance.Command
        {
            TeamId = team.Id,
            ApplicationId = application.Id
        });

        Assert.Equal("Template Name", created.Name);
        Assert.Equal("https://example.test/template", created.Url);
        Assert.Equal(template.Icon, created.Icon);
        Assert.True(created.Embeddable);
    }

    /// <summary>
    /// The team and view placeholders are substituted when the instance is read, which is how one
    /// application serves every team in a view.
    /// </summary>
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

        var created = await SendAsync(new CreateApplicationInstance.Command
        {
            TeamId = team.Id,
            ApplicationId = application.Id
        });

        Assert.Equal("Blue Team console", created.Name);
        Assert.Equal(
            $"https://example.test/{view.Id}/{team.Id}?view=My%20View&team=Blue%20Team",
            created.Url);
    }

    /// <summary>
    /// An instance is a placement within one view, so a cross-view pairing is refused.
    /// </summary>
    [Fact]
    public async Task CreateApplicationInstance_refuses_a_team_and_application_in_different_views()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id);
        var application = TestData.Application(otherView.Id);
        await Seed(view, otherView, team, application);

        await Assert.ThrowsAsync<ConflictException>(() => SendAsync(new CreateApplicationInstance.Command
        {
            TeamId = team.Id,
            ApplicationId = application.Id
        }));
    }

    [Fact]
    public async Task CreateApplicationInstance_reports_a_missing_team_as_not_found()
    {
        var view = TestData.View();
        var application = TestData.Application(view.Id);
        await Seed(view, application);

        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new CreateApplicationInstance.Command
            {
                TeamId = Guid.NewGuid(),
                ApplicationId = application.Id
            }));
    }

    [Fact]
    public async Task CreateApplicationInstance_reports_a_missing_application_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<EntityNotFoundException<Application>>(
            () => SendAsync(new CreateApplicationInstance.Command
            {
                TeamId = team.Id,
                ApplicationId = Guid.NewGuid()
            }));
    }

    [Fact]
    public async Task CreateApplicationInstance_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        await Seed(view, team, application);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new CreateApplicationInstance.Command
            {
                TeamId = team.Id,
                ApplicationId = application.Id
            }));
    }

    [Fact]
    public async Task EditApplicationInstance_moves_it_to_another_application()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var first = TestData.Application(view.Id, "First");
        var second = TestData.Application(view.Id, "Second");
        var instance = TestData.ApplicationInstance(team.Id, first.Id);
        await Seed(view, team, first, second, instance);

        var edited = await SendAsync(new EditApplicationInstance.Command
        {
            Id = instance.Id,
            TeamId = team.Id,
            ApplicationId = second.Id,
            DisplayOrder = 7
        });

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

        await Assert.ThrowsAsync<EntityNotFoundException<ApplicationInstance>>(
            () => SendAsync(new EditApplicationInstance.Command
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                ApplicationId = application.Id
            }));
    }

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

        await Assert.ThrowsAsync<ConflictException>(() => SendAsync(new EditApplicationInstance.Command
        {
            Id = instance.Id,
            TeamId = team.Id,
            ApplicationId = elsewhere.Id
        }));
    }

    [Fact]
    public async Task DeleteApplicationInstance_removes_the_instance_and_leaves_the_application()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        await SendAsync(new DeleteApplicationInstance.Command { Id = instance.Id });

        await using var db = NewContext();
        Assert.False(await db.ApplicationInstances.AnyAsync(Ct));
        Assert.True(await db.Applications.AnyAsync(x => x.Id == application.Id, Ct));
    }

    [Fact]
    public async Task DeleteApplicationInstance_reports_a_missing_instance_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<ApplicationInstance>>(
            () => SendAsync(new DeleteApplicationInstance.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task DeleteApplicationInstance_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new DeleteApplicationInstance.Command { Id = instance.Id }));
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

        var result = await SendAsync(new GetApplicationInstance.Query { Id = instance.Id });

        Assert.Equal("Findable", result.Name);
        Assert.Equal(view.Id, result.ViewId);
    }

    /// <summary>
    /// Characterizes current behaviour: a missing id returns null rather than throwing.
    /// </summary>
    [Fact]
    public async Task GetApplicationInstance_returns_nothing_for_a_missing_instance()
    {
        Assert.Null(await SendAsync(new GetApplicationInstance.Query { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// Readable by the team itself: <c>ViewTeam</c> on the instance's own team is enough.
    /// </summary>
    [Fact]
    public async Task GetApplicationInstance_is_allowed_for_a_caller_holding_ViewTeam_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        Assert.Equal(
            instance.Id,
            (await SendAsync(caller, new GetApplicationInstance.Query { Id = instance.Id })).Id);
    }

    [Fact]
    public async Task GetApplicationInstance_is_forbidden_for_a_caller_on_another_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var otherTeam = TestData.Team(view.Id, "Other");
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, otherTeam, application, instance);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, otherTeam.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(caller, new GetApplicationInstance.Query { Id = instance.Id }));
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

        var instances = await SendAsync(
            new GetApplicationInstancesByTeam.Query { TeamId = team.Id });

        Assert.Equal(["First", "Second"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task GetApplicationInstancesByTeam_reports_a_missing_team_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new GetApplicationInstancesByTeam.Query { TeamId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetApplicationInstancesByTeam_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetApplicationInstancesByTeam.Query { TeamId = team.Id }));
    }

    // ---- MoveApplicationInstance ----------------------------------------------------------------

    /// <summary>
    /// Moving up renumbers the whole team's instances by position, so the returned order is what the
    /// team will see next.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_moves_an_instance_up_one_place()
    {
        var (team, ids) = await SeedOrderedInstances("A", "B", "C");

        var instances = await SendAsync(new MoveApplicationInstance.Command
        {
            Id = ids["B"],
            Direction = MoveApplicationInstance.Direction.Up
        });

        Assert.Equal(["B", "A", "C"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task MoveApplicationInstance_moves_an_instance_down_one_place()
    {
        var (team, ids) = await SeedOrderedInstances("A", "B", "C");

        var instances = await SendAsync(new MoveApplicationInstance.Command
        {
            Id = ids["B"],
            Direction = MoveApplicationInstance.Direction.Down
        });

        Assert.Equal(["A", "C", "B"], instances.Select(x => x.Name));
    }

    /// <summary>
    /// The first instance has nowhere to go, and the pass still leaves the order intact.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_leaves_the_first_instance_where_it_is()
    {
        var (team, ids) = await SeedOrderedInstances("A", "B", "C");

        var instances = await SendAsync(new MoveApplicationInstance.Command
        {
            Id = ids["A"],
            Direction = MoveApplicationInstance.Direction.Up
        });

        Assert.Equal(["A", "B", "C"], instances.Select(x => x.Name));
    }

    [Fact]
    public async Task MoveApplicationInstance_leaves_the_last_instance_where_it_is()
    {
        var (team, ids) = await SeedOrderedInstances("A", "B", "C");

        var instances = await SendAsync(new MoveApplicationInstance.Command
        {
            Id = ids["C"],
            Direction = MoveApplicationInstance.Direction.Down
        });

        Assert.Equal(["A", "B", "C"], instances.Select(x => x.Name));
    }

    /// <summary>
    /// Only the moved instance's own team is renumbered.
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

        await SendAsync(new MoveApplicationInstance.Command
        {
            Id = mine.Id,
            Direction = MoveApplicationInstance.Direction.Down
        });

        await using var db = NewContext();
        Assert.Equal(5, (await db.ApplicationInstances.SingleAsync(x => x.Id == theirs.Id, Ct)).DisplayOrder);
    }

    [Fact]
    public async Task MoveApplicationInstance_reports_a_missing_instance_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<ApplicationInstance>>(
            () => SendAsync(new MoveApplicationInstance.Command { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// Reordering is a team-level change, so <c>ManageTeam</c> on the instance's team is enough.
    /// </summary>
    [Fact]
    public async Task MoveApplicationInstance_is_allowed_for_a_caller_holding_ManageTeam()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        Assert.Single(await SendAsync(caller, new MoveApplicationInstance.Command { Id = instance.Id }));
    }

    [Fact]
    public async Task MoveApplicationInstance_is_forbidden_for_a_caller_holding_only_ViewTeam()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var application = TestData.Application(view.Id);
        var instance = TestData.ApplicationInstance(team.Id, application.Id);
        await Seed(view, team, application, instance);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(caller, new MoveApplicationInstance.Command { Id = instance.Id }));
    }

    /// <summary>
    /// Seeds one team with an instance per name, in the order given, and returns the instance ids by
    /// application name.
    /// </summary>
    private async Task<(TeamEntity Team, Dictionary<string, Guid> InstanceIds)> SeedOrderedInstances(
        params string[] names)
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

        return (team, ids);
    }
}
