// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// Covers what <c>Clone</c> fans out over: applications and their per-team instances, team permission
/// assignments, file pointers, and the application urls that reference those files.
/// </summary>
/// <remarks>
/// The name, description, default team and scoped-permission paths are already covered in
/// <see cref="ViewRequestTests"/>. Clone matches a copy back to its original by <c>Name</c> in three
/// places, so several tests here pin what happens when two teams or two applications share one.
/// </remarks>
public class ViewCloneTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- The cloned view ------------------------------------------------------------------------

    [Fact]
    public async Task Cloning_records_the_source_view_as_the_parent_and_takes_a_fresh_creation_date()
    {
        var view = TestData.View();
        await Seed(view);

        var clone = await CloneView(view.Id);

        Assert.Equal(view.Id, clone.ParentViewId);
        Assert.True(clone.DateCreated > TestData.DefaultDateCreated);
    }

    /// <summary>
    /// This is wrong: a view id naming nothing should be a 404, and is a server error instead.
    /// </summary>
    /// <remarks>
    /// Issue 38. <c>Clone.cs:75</c> loads the view with <c>SingleOrDefaultAsync</c> and <c>:77</c>
    /// dereferences it unguarded, where every other handler in the feature throws
    /// <c>EntityNotFoundException&lt;View&gt;</c>. Turns red when the null is checked.
    /// </remarks>
    [Fact]
    public async Task Cloning_a_view_that_does_not_exist_fails_instead_of_reporting_not_found()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync($"api/views/{Guid.NewGuid()}/clone", new { }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Equal("Object reference not set to an instance of an object.", problem.Detail);
    }

    /// <summary>
    /// This is wrong: <c>CreateViews</c> alone clones any view by id, and the 201 body hands the caller
    /// the name and description of a view the same caller is forbidden to <c>Get</c>.
    /// </summary>
    /// <remarks>
    /// Issue 37. <c>Clone.cs:61</c> authorizes on the system permission with empty view and team lists,
    /// so nothing is scoped to the source view and nothing bounds how many copies one caller makes.
    /// Turns red when a read check is added.
    /// </remarks>
    [Fact]
    public async Task Cloning_is_allowed_for_a_caller_who_cannot_read_the_source_view()
    {
        var view = TestData.View("Someone elses");
        view.Description = "Not for you";
        await Seed(view, TestData.Team(view.Id, "Alpha"));

        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateViews).SeedAsync();

        // The inconsistency is the finding, so the refusal this caller gets from Get is arranged
        // alongside the clone they are allowed rather than left to another test.
        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/views/{view.Id}", Ct));

        var clone = await ReadAsync<View>(await Client(actor).PostAsJsonAsync(
            $"api/views/{view.Id}/clone", new { }, Ct));

        Assert.Equal("Clone of Someone elses", clone.Name);
        Assert.Equal("Not for you", clone.Description);

        await using var db = NewContext();
        Assert.Equal("Alpha", (await db.Teams.SingleAsync(x => x.ViewId == clone.Id, Ct)).Name);
    }

    // ---- Applications ---------------------------------------------------------------------------

    [Fact]
    public async Task Cloning_a_view_copies_its_applications()
    {
        var view = TestData.View();
        var template = TestData.ApplicationTemplate();
        var portal = TestData.Application(view.Id, "Portal", "https://player.test/portal");
        var console = TestData.Application(view.Id, "Console", "https://player.test/console", template.Id);
        console.Icon = "https://player.test/icon.png";
        await Seed(view, template, portal, console);

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var cloned = await db.Applications.Where(x => x.ViewId == clone.Id).ToListAsync(Ct);

        Assert.Equal(new[] { "Console", "Portal" }, cloned.Select(x => x.Name).Order());
        Assert.DoesNotContain(cloned, x => x.Id == portal.Id || x.Id == console.Id);

        var clonedConsole = cloned.Single(x => x.Name == "Console");
        Assert.Equal("https://player.test/console", clonedConsole.Url);
        Assert.Equal("https://player.test/icon.png", clonedConsole.Icon);
        Assert.Equal(template.Id, clonedConsole.ApplicationTemplateId);
    }

    [Fact]
    public async Task Cloning_repoints_team_application_instances_at_the_cloned_applications()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Alpha");
        var portal = TestData.Application(view.Id, "Portal");
        var console = TestData.Application(view.Id, "Console");
        await Seed(
            view,
            team,
            portal,
            console,
            TestData.ApplicationInstance(team.Id, portal.Id, 2),
            TestData.ApplicationInstance(team.Id, console.Id, 1));

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedApps = await db.Applications.Where(x => x.ViewId == clone.Id).ToListAsync(Ct);
        var clonedTeam = await db.Teams
            .Include(x => x.Applications)
            .SingleAsync(x => x.ViewId == clone.Id, Ct);

        var byName = clonedTeam.Applications
            .ToDictionary(x => clonedApps.Single(a => a.Id == x.ApplicationId).Name);

        // Instances left pointing at the original's applications break the moment it is deleted.
        Assert.Equal(2, byName["Portal"].DisplayOrder);
        Assert.Equal(1, byName["Console"].DisplayOrder);
        Assert.All(clonedTeam.Applications, x => Assert.Equal(clonedTeam.Id, x.TeamId));
    }

    /// <summary>
    /// An application with no name of its own is matched through its template's name. Two of them are
    /// told apart only by their templates, so each instance must land on a different copy.
    /// </summary>
    [Fact]
    public async Task Cloning_matches_a_template_backed_application_by_its_template_name()
    {
        var view = TestData.View();
        var consoleTemplate = TestData.ApplicationTemplate("Shared Console");
        var desktopTemplate = TestData.ApplicationTemplate("Shared Desktop");
        var console = TestData.Application(view.Id, name: null, url: null, templateId: consoleTemplate.Id);
        var desktop = TestData.Application(view.Id, name: null, url: null, templateId: desktopTemplate.Id);
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(
            view,
            consoleTemplate,
            desktopTemplate,
            console,
            desktop,
            team,
            TestData.ApplicationInstance(team.Id, console.Id),
            TestData.ApplicationInstance(team.Id, desktop.Id));

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedApps = await db.Applications.Where(x => x.ViewId == clone.Id).ToListAsync(Ct);
        var clonedTeam = await db.Teams
            .Include(x => x.Applications)
            .SingleAsync(x => x.ViewId == clone.Id, Ct);

        var clonedConsole = clonedApps.Single(x => x.ApplicationTemplateId == consoleTemplate.Id);
        var clonedDesktop = clonedApps.Single(x => x.ApplicationTemplateId == desktopTemplate.Id);

        Assert.All(clonedApps, x => Assert.Null(x.Name));
        Assert.Equal(
            new[] { clonedConsole.Id, clonedDesktop.Id }.Order(),
            clonedTeam.Applications.Select(x => x.ApplicationId).Order());
    }

    /// <summary>
    /// This is wrong: nothing stops two applications in a view sharing a name, and when they do every
    /// instance is repointed at the first copy while the second is left unused.
    /// </summary>
    /// <remarks>
    /// Issue 35. <c>Clone.cs:106</c> resolves the original application by id and <c>:107</c> throws that
    /// away to search the copies by <c>GetName()</c>. Turns red — as intended — when Clone keys off the
    /// original application's id, which gives one instance per application.
    /// </remarks>
    [Fact]
    public async Task Cloning_points_every_instance_at_one_application_when_two_share_a_name()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Alpha");
        var first = TestData.Application(view.Id, "Portal", "https://player.test/one");
        var second = TestData.Application(view.Id, "Portal", "https://player.test/two");
        await Seed(
            view,
            team,
            first,
            second,
            TestData.ApplicationInstance(team.Id, first.Id),
            TestData.ApplicationInstance(team.Id, second.Id));

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedApps = await db.Applications.Where(x => x.ViewId == clone.Id).ToListAsync(Ct);
        var clonedTeam = await db.Teams
            .Include(x => x.Applications)
            .SingleAsync(x => x.ViewId == clone.Id, Ct);

        Assert.Equal(2, clonedApps.Count);
        Assert.Equal(2, clonedTeam.Applications.Count);

        // Order-independent, so it does not depend on which duplicate EF returns first.
        Assert.Single(clonedTeam.Applications.Select(x => x.ApplicationId).Distinct());
    }

    // ---- Team permissions -----------------------------------------------------------------------

    [Fact]
    public async Task Cloning_copies_each_teams_permission_assignments()
    {
        var view = TestData.View();
        var alpha = TestData.Team(view.Id, "Alpha");
        var bravo = TestData.Team(view.Id, "Bravo");
        await Seed(
            view,
            alpha,
            bravo,
            new TeamPermissionAssignmentEntity(alpha.Id, TestData.TeamPermissions.ViewTeam),
            new TeamPermissionAssignmentEntity(alpha.Id, TestData.TeamPermissions.UploadViewIsos),
            new TeamPermissionAssignmentEntity(bravo.Id, TestData.TeamPermissions.ViewTeam));

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedTeams = await db.Teams
            .Include(x => x.Permissions)
            .Where(x => x.ViewId == clone.Id)
            .ToListAsync(Ct);

        var clonedAlpha = clonedTeams.Single(x => x.Name == "Alpha");
        var clonedBravo = clonedTeams.Single(x => x.Name == "Bravo");

        // Clone builds each row with Guid.Empty for the team id and relies on EF fixup, so reading the
        // rows back is the assertion that matters — (TeamId, PermissionId) is unique.
        Assert.Equal(
            new[] { TestData.TeamPermissions.ViewTeam, TestData.TeamPermissions.UploadViewIsos }.Order(),
            clonedAlpha.Permissions.Select(x => x.PermissionId).Order());
        Assert.Equal(TestData.TeamPermissions.ViewTeam, Assert.Single(clonedBravo.Permissions).PermissionId);
        Assert.All(clonedAlpha.Permissions, x => Assert.Equal(clonedAlpha.Id, x.TeamId));
    }

    // ---- Files ----------------------------------------------------------------------------------

    [Fact]
    public async Task Cloning_copies_the_file_rows_and_leaves_them_pointing_at_the_same_bytes()
    {
        var view = TestData.View();
        await Seed(view);
        var file = await SeedFile(view, "notes.txt");

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedFile = await db.Files.SingleAsync(x => x.View.Id == clone.Id, Ct);

        // Two rows over one path, so deleting either view's copy must not unlink the other's bytes.
        Assert.NotEqual(file.Id, clonedFile.Id);
        Assert.Equal(file.Path, clonedFile.Path);
        Assert.Equal("notes.txt", clonedFile.Name);
        Assert.Empty(clonedFile.TeamIds);
        Assert.True(await db.Files.AnyAsync(x => x.Id == file.Id && x.View.Id == view.Id, Ct));
    }

    /// <summary>
    /// A file is visible to the teams it names, so ids left pointing into the original view would expose
    /// the clone's file to the wrong teams and hide it from its own.
    /// </summary>
    [Fact]
    public async Task Cloning_remaps_file_team_ids_onto_the_cloned_teams()
    {
        var view = TestData.View();
        var alpha = TestData.Team(view.Id, "Alpha");
        var bravo = TestData.Team(view.Id, "Bravo");
        await Seed(view, alpha, bravo);
        var file = await SeedFile(view, "notes.txt", alpha.Id);

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedAlpha = await db.Teams.SingleAsync(x => x.ViewId == clone.Id && x.Name == "Alpha", Ct);
        var clonedFile = await db.Files.SingleAsync(x => x.View.Id == clone.Id, Ct);

        Assert.Equal(clonedAlpha.Id, Assert.Single(clonedFile.TeamIds));

        // FileEntity.Clone shares the TeamIds list instance; the original only keeps its ids because
        // Clone assigns a new list to the copy.
        Assert.Equal(alpha.Id, Assert.Single((await db.Files.SingleAsync(x => x.Id == file.Id, Ct)).TeamIds));
    }

    /// <summary>
    /// This is wrong: two teams may share a name, and then both of a file's ids remap onto the first
    /// copy, so the second cloned team loses access to a file it should see.
    /// </summary>
    /// <remarks>
    /// Issue 35. <c>Clone.cs:154-155</c> remaps through the team's name rather than the
    /// <c>clonedTeams</c> dictionary the handler already built. Turns red — as intended — when the remap
    /// goes through the original team id, which gives one id per team.
    /// </remarks>
    [Fact]
    public async Task Cloning_collapses_file_team_ids_when_two_teams_share_a_name()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "Alpha");
        var second = TestData.Team(view.Id, "Alpha");
        await Seed(view, first, second);
        await SeedFile(view, "notes.txt", first.Id, second.Id);

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedTeamIds = await db.Teams.Where(x => x.ViewId == clone.Id).Select(x => x.Id).ToListAsync(Ct);
        var clonedFile = await db.Files.SingleAsync(x => x.View.Id == clone.Id, Ct);

        Assert.Equal(2, clonedTeamIds.Count);
        Assert.Equal(2, clonedFile.TeamIds.Count);

        // Order-independent, so it does not depend on which duplicate EF returns first.
        Assert.Single(clonedFile.TeamIds.Distinct());
        Assert.Single(clonedTeamIds, x => clonedFile.TeamIds.Contains(x));
    }

    /// <summary>
    /// This is wrong twice over: deleting a team leaves its id in <c>File.TeamIds</c>, and the remap then
    /// dereferences the team it cannot find after the copy has already been committed.
    /// </summary>
    /// <remarks>
    /// Issue 36. <c>Teams/Requests/Delete.cs</c> never scrubs <c>File.TeamIds</c>, <c>Clone.cs:154</c>
    /// reads <c>.Name</c> off a <c>FirstOrDefault</c> that found nothing, and the save at <c>:133</c>
    /// shares no transaction with the one at <c>:193</c> — so the 500 leaves a view behind whose file
    /// still names a team in the source view, and the source can never be cloned again. Turns red when
    /// the remap is guarded, when the deletion scrubs the id, or when the two saves become atomic.
    /// </remarks>
    [Fact]
    public async Task Cloning_fails_and_leaves_a_half_cloned_view_when_a_file_names_a_deleted_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Alpha");
        var doomed = TestData.Team(view.Id, "Gone");
        await Seed(view, team, doomed);
        await SeedFile(view, "notes.txt", team.Id, doomed.Id);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/teams/{doomed.Id}", Ct));

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync($"api/views/{view.Id}/clone", new { }, Ct));

        Assert.Equal("Object reference not set to an instance of an object.", problem.Detail);

        await using var db = NewContext();
        var orphan = await db.Views.SingleAsync(x => x.ParentViewId == view.Id, Ct);
        var orphanFile = await db.Files.SingleAsync(x => x.View.Id == orphan.Id, Ct);

        Assert.Contains(team.Id, orphanFile.TeamIds);
    }

    // ---- Application urls -----------------------------------------------------------------------

    /// <summary>
    /// An application url embedding a file id would download the original view's file, so the id is
    /// rewritten to the copy's. Urls naming no file, and applications with no url, are left alone.
    /// </summary>
    [Fact]
    public async Task Cloning_rewrites_application_urls_that_point_at_a_copied_file()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Alpha");
        await Seed(view, team);
        var file = await SeedFile(view, "notes.txt", team.Id);

        await Seed(
            TestData.Application(view.Id, "Linked", $"https://player.test/api/files/{file.Id}/download"),
            TestData.Application(view.Id, "Plain", "https://player.test/other"),
            TestData.Application(view.Id, "Inherited", url: null));

        var clone = await CloneView(view.Id);

        await using var db = NewContext();
        var clonedFile = await db.Files.SingleAsync(x => x.View.Id == clone.Id, Ct);
        var cloned = (await db.Applications.Where(x => x.ViewId == clone.Id).ToListAsync(Ct))
            .ToDictionary(x => x.Name);

        Assert.Equal($"https://player.test/api/files/{clonedFile.Id}/download", cloned["Linked"].Url);
        Assert.Equal("https://player.test/other", cloned["Plain"].Url);
        Assert.Null(cloned["Inherited"].Url);

        var original = await db.Applications.SingleAsync(x => x.ViewId == view.Id && x.Name == "Linked", Ct);
        Assert.Contains(file.Id.ToString(), original.Url);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>Clones a view as <c>Root</c>, for the tests that are about what Clone copies.</summary>
    private async Task<View> CloneView(Guid viewId) =>
        await ReadAsync<View>(await RootClient.PostAsJsonAsync($"api/views/{viewId}/clone", new { }, Ct));

    /// <summary>
    /// A file row in <paramref name="view"/>. The path is never opened — cloning copies pointers, not
    /// bytes — so the row is seeded rather than uploaded through <c>POST api/files</c>.
    /// </summary>
    private async Task<FileEntity> SeedFile(ViewEntity view, string name, params Guid[] teamIds)
    {
        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"/tmp/player-tests/{Guid.NewGuid():N}/{name}",
            TeamIds = [.. teamIds],
            View = view
        };

        await Seed(file);
        return file;
    }
}
