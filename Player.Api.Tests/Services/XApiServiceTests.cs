// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Options;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Services;

/// <summary>
/// Turns a user's activity into xAPI statements on the queue the background sender drains. Every emit
/// is fire-and-forget — the whole body sits inside a try/catch — so a failure here is silent, and what
/// these tests pin is which paths produce a statement and what that statement says.
/// </summary>
public class XApiServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private const string Issuer = "https://identity.test";
    private const string ApiUrl = "https://player.test/api";
    private const string TeamExtensionKey = "https://crucible.sei.cmu.edu/xapi/extensions/team";
    private const string ProfileCategory = "https://crucible.sei.cmu.edu/xapi/profile/v1";

    // ---- The configuration switch ------------------------------------------------------------------

    /// <summary>
    /// The harness leaves xAPI unconfigured, which is what a deployment that never set it up looks like.
    /// </summary>
    [Fact]
    public void IsConfigured_is_false_by_default()
    {
        Assert.False(HostFor(Caller()).Resolve<IXApiService>().IsConfigured());
    }

    /// <summary>A username is the credential for the LRS, so enabling alone is not enough.</summary>
    [Fact]
    public void IsConfigured_is_false_when_enabled_without_a_username()
    {
        Assert.False(Service(Caller(), o => o.Username = null).IsConfigured());
    }

    [Fact]
    public void IsConfigured_is_false_when_the_username_is_whitespace()
    {
        Assert.False(Service(Caller(), o => o.Username = "   ").IsConfigured());
    }

    [Fact]
    public void IsConfigured_is_false_when_a_username_is_set_but_the_feature_is_off()
    {
        Assert.False(Service(Caller(), o => o.Enabled = false).IsConfigured());
    }

    [Fact]
    public void IsConfigured_is_true_when_enabled_with_a_username()
    {
        Assert.True(Service(Caller()).IsConfigured());
    }

    /// <summary>
    /// Every entry point checks the switch before doing any work, so an unconfigured deployment pays
    /// nothing and queues nothing.
    /// </summary>
    [Fact]
    public async Task Nothing_is_queued_when_xapi_is_not_configured()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await EmitEverything(HostFor(Caller()).Resolve<IXApiService>(), view.Id, team.Id);

        Assert.Empty(await Db.XApiQueuedStatements.ToListAsync(Ct));
    }

    // ---- The actor -------------------------------------------------------------------------------

    /// <summary>
    /// The statement is attributed to the user's stored name and their token subject, which is how the
    /// LRS ties statements from different Crucible apps to one person.
    /// </summary>
    [Fact]
    public async Task Statements_name_the_user_from_their_row_and_the_sub_claim()
    {
        var view = TestData.View();
        var user = TestData.User(name: "Ada Lovelace");
        await Seed(view, user);
        var caller = Caller(user.Id);

        await Service(caller).EmitViewViewedAsync(view.Id, Ct);

        var actor = Json(await Queued())["actor"];
        Assert.Equal("Ada Lovelace", actor["name"].GetValue<string>());
        Assert.Equal(user.Id.ToString(), actor["account"]["name"].GetValue<string>());
    }

    /// <summary>
    /// A statement still goes out for a subject with no user row — the identity provider is the source
    /// of truth for who is calling, not the local table.
    /// </summary>
    [Fact]
    public async Task Statements_fall_back_to_Unknown_User_when_the_caller_has_no_user_row()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitViewViewedAsync(view.Id, Ct);

        Assert.Equal("Unknown User", Json(await Queued())["actor"]["name"].GetValue<string>());
    }

    /// <summary>
    /// The configured issuer wins over the token's own, so one deployment reports a single account
    /// home page even when tokens arrive from several issuer host names.
    /// </summary>
    [Fact]
    public async Task Account_home_page_prefers_the_configured_issuer_url()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller(issuer: "https://token-issuer.test"), o => o.IssuerUrl = "https://configured.test")
            .EmitViewViewedAsync(view.Id, Ct);

        Assert.Equal("https://configured.test/", HomePage(Json(await Queued())));
    }

    [Fact]
    public async Task Account_home_page_falls_back_to_the_iss_claim()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller(issuer: "https://token-issuer.test")).EmitViewViewedAsync(view.Id, Ct);

        Assert.Equal("https://token-issuer.test/", HomePage(Json(await Queued())));
    }

    /// <summary>
    /// An <c>iss</c> that is a bare host is not a usable account home page, so it is promoted to a URL.
    /// </summary>
    [Fact]
    public async Task Account_home_page_prefixes_a_scheme_onto_a_bare_issuer_host()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller(issuer: "identity.test")).EmitViewViewedAsync(view.Id, Ct);

        Assert.Equal("http://identity.test/", HomePage(Json(await Queued())));
    }

    /// <summary>
    /// The actor is built once and reused for the life of the service, so a name change mid-request is
    /// not picked up. Harmless — the service is scoped to one request.
    /// </summary>
    [Fact]
    public async Task The_actor_is_built_once_and_reused()
    {
        var view = TestData.View();
        var user = TestData.User(name: "Before");
        await Seed(view, user);
        var service = Service(Caller(user.Id));

        await service.EmitViewViewedAsync(view.Id, Ct);
        user.Name = "After";
        await Db.SaveChangesAsync(Ct);
        await service.EmitViewViewedAsync(view.Id, Ct);

        var names = await Db.XApiQueuedStatements
            .Select(x => x.StatementJson)
            .ToListAsync(Ct);
        Assert.Equal(["Before", "Before"], names.Select(x => JsonNode.Parse(x)["actor"]["name"].GetValue<string>()));
    }

    /// <summary>
    /// Characterizes issue 27: the <c>iss</c> claim is read with <c>First</c>, so a token without one
    /// throws before any statement is built and the failure is swallowed by the catch. Flip these to
    /// expect a queued statement when the lookup becomes tolerant of a missing claim.
    /// </summary>
    [Fact]
    public async Task Nothing_is_queued_when_the_token_carries_no_iss_claim()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await EmitEverything(Service(Caller(issuer: null)), view.Id, team.Id);

        Assert.Empty(await Db.XApiQueuedStatements.ToListAsync(Ct));
    }

    /// <summary>
    /// Nothing propagates out of an emit. A misconfigured issuer URL fails inside the try on every
    /// entry point, and the caller — a request handler doing real work — is unaffected.
    /// </summary>
    [Fact]
    public async Task An_unparseable_issuer_url_is_swallowed_and_queues_nothing()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await EmitEverything(Service(Caller(), o => o.IssuerUrl = "not a url"), view.Id, team.Id);

        Assert.Empty(await Db.XApiQueuedStatements.ToListAsync(Ct));
    }

    // ---- Shared statement context ------------------------------------------------------------------

    /// <summary>
    /// Every statement registers against the view and carries the Crucible profile category, which is
    /// how an LRS query separates Player's statements from another tool's.
    /// </summary>
    [Fact]
    public async Task Statements_carry_the_view_registration_the_platform_and_the_profile_category()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller(), o => o.Platform = "Player Test").EmitViewViewedAsync(view.Id, Ct);

        var context = Json(await Queued())["context"];
        Assert.Equal(view.Id.ToString(), context["registration"].GetValue<string>());
        Assert.Equal("Player Test", context["platform"].GetValue<string>());
        Assert.Equal("en-US", context["language"].GetValue<string>());
        var category = Assert.Single(context["contextActivities"]["category"].AsArray());
        Assert.Equal(ProfileCategory, category["id"].GetValue<string>());
    }

    // ---- Viewing a view ---------------------------------------------------------------------------

    [Fact]
    public async Task EmitViewViewed_queues_a_viewed_statement_naming_the_view()
    {
        var view = TestData.View(name: "Cyber Range");
        view.Description = "Range description";
        await Seed(view);

        await Service(Caller()).EmitViewViewedAsync(view.Id, Ct);

        var queued = await Queued();
        Assert.Equal("viewed", queued.Verb);
        Assert.Equal($"{ApiUrl}/views/{view.Id}", queued.ActivityId);
        Assert.Equal(view.Id, queued.ViewId);

        var statement = Json(queued);
        Assert.Equal("http://id.tincanapi.com/verb/viewed", statement["verb"]["id"].GetValue<string>());
        Assert.Equal("viewed", statement["verb"]["display"]["en-US"].GetValue<string>());
        Assert.Equal($"{ApiUrl}/views/{view.Id}", statement["object"]["id"].GetValue<string>());

        var definition = statement["object"]["definition"];
        Assert.Equal("http://adlnet.gov/expapi/activities/simulation", definition["type"].GetValue<string>());
        Assert.Equal("Cyber Range", definition["name"]["en-US"].GetValue<string>());
        Assert.Equal("Range description", definition["description"]["en-US"].GetValue<string>());
    }

    /// <summary>
    /// A view is required to have neither, and the LRS activity definition is required to have both.
    /// </summary>
    [Fact]
    public async Task EmitViewViewed_substitutes_placeholders_for_a_view_with_no_name_or_description()
    {
        var view = TestData.View(name: null);
        await Seed(view);

        await Service(Caller()).EmitViewViewedAsync(view.Id, Ct);

        var definition = Json(await Queued())["object"]["definition"];
        Assert.Equal("Unnamed View", definition["name"]["en-US"].GetValue<string>());
        Assert.Equal($"Exercise workspace for View {view.Id}", definition["description"]["en-US"].GetValue<string>());
    }

    /// <summary>
    /// The statement describes the view, so without one there is nothing to describe.
    /// </summary>
    [Fact]
    public async Task EmitViewViewed_queues_nothing_for_a_view_that_does_not_exist()
    {
        await Service(Caller()).EmitViewViewedAsync(Guid.NewGuid(), Ct);

        Assert.Empty(await Db.XApiQueuedStatements.ToListAsync(Ct));
    }

    /// <summary>
    /// The team comes from the membership row rather than the caller's claims, so it reports which team
    /// the user is actually acting as in that view.
    /// </summary>
    [Fact]
    public async Task EmitViewViewed_carries_the_callers_primary_team_as_a_context_extension()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);
        await MakeMember(view, team, user);

        await Service(Caller(user.Id)).EmitViewViewedAsync(view.Id, Ct);

        Assert.Equal(team.Id.ToString(), TeamExtension(Json(await Queued())));
    }

    /// <summary>
    /// A caller with no membership — an administrator looking in — still produces a statement, just
    /// without a team.
    /// </summary>
    [Fact]
    public async Task EmitViewViewed_omits_the_team_extension_when_the_caller_is_not_a_member()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitViewViewedAsync(view.Id, Ct);

        Assert.Null(TeamExtension(Json(await Queued())));
    }

    /// <summary>
    /// A membership with no primary team yet reports no team, rather than guessing one.
    /// </summary>
    [Fact]
    public async Task EmitViewViewed_omits_the_team_extension_when_the_membership_has_no_primary_team()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        await Service(Caller(user.Id)).EmitViewViewedAsync(view.Id, Ct);

        Assert.Null(TeamExtension(Json(await Queued())));
    }

    // ---- Switching applications --------------------------------------------------------------------

    /// <summary>
    /// The application is a module nested under the view, so the view is named as the parent activity —
    /// that nesting is what lets an LRS roll app usage up to a view.
    /// </summary>
    [Fact]
    public async Task EmitApplicationSwitched_queues_an_accessed_statement_parented_to_the_view()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitApplicationSwitchedAsync(view.Id, "vm", "https://vm.test", Ct);

        var queued = await Queued();
        Assert.Equal("accessed", queued.Verb);
        Assert.Equal($"{ApiUrl}/views/{view.Id}/apps/vm", queued.ActivityId);
        Assert.Equal(view.Id, queued.ViewId);

        var statement = Json(queued);
        Assert.Equal("http://activitystrea.ms/schema/1.0/access", statement["verb"]["id"].GetValue<string>());
        Assert.Equal("accessed", statement["verb"]["display"]["en-US"].GetValue<string>());
        Assert.Equal("http://adlnet.gov/expapi/activities/module", statement["object"]["definition"]["type"].GetValue<string>());

        var parent = Assert.Single(statement["context"]["contextActivities"]["parent"].AsArray());
        Assert.Equal($"{ApiUrl}/views/{view.Id}", parent["id"].GetValue<string>());
        Assert.Equal("http://adlnet.gov/expapi/activities/simulation", parent["definition"]["type"].GetValue<string>());
    }

    /// <summary>
    /// The known applications get a human-readable name, matched case-insensitively because the caller
    /// passes whatever the UI happens to be using.
    /// </summary>
    [Theory]
    [InlineData("cite", "CITE Risk Assessment")]
    [InlineData("CITE", "CITE Risk Assessment")]
    [InlineData("gallery", "Gallery Articles")]
    [InlineData("steamfitter", "Steamfitter Tasks")]
    [InlineData("vm", "VM Console")]
    [InlineData("console", "Terminal Console")]
    [InlineData("player", "Player Dashboard")]
    [InlineData("Admin", "Admin Panel")]
    public async Task EmitApplicationSwitched_maps_a_known_application_to_its_display_name(
        string applicationName,
        string expected)
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitApplicationSwitchedAsync(view.Id, applicationName, null, Ct);

        Assert.Equal(expected, Json(await Queued())["object"]["definition"]["name"]["en-US"].GetValue<string>());
    }

    /// <summary>
    /// An application outside the table — anything added to a view by hand — is reported under its own
    /// name rather than dropped.
    /// </summary>
    [Fact]
    public async Task EmitApplicationSwitched_uses_the_raw_name_for_an_unknown_application()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitApplicationSwitchedAsync(view.Id, "Wireshark", null, Ct);

        var statement = Json(await Queued());
        Assert.Equal("Wireshark", statement["object"]["definition"]["name"]["en-US"].GetValue<string>());
        Assert.Equal($"{ApiUrl}/views/{view.Id}/apps/wireshark", statement["object"]["id"].GetValue<string>());
    }

    /// <summary>
    /// The activity id is lower-cased and URL-encoded, so a display name with spaces still yields a
    /// legal activity id.
    /// </summary>
    [Fact]
    public async Task EmitApplicationSwitched_lower_cases_and_url_encodes_the_activity_id()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitApplicationSwitchedAsync(view.Id, "My App/1", null, Ct);

        Assert.Equal(
            $"{ApiUrl}/views/{view.Id}/apps/my+app%2f1",
            Json(await Queued())["object"]["id"].GetValue<string>());
    }

    /// <summary>
    /// A null name is tolerated rather than throwing: the emit is called from a hub where the client
    /// supplies the name.
    /// </summary>
    [Fact]
    public async Task EmitApplicationSwitched_falls_back_to_a_placeholder_for_a_null_application_name()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitApplicationSwitchedAsync(view.Id, null, null, Ct);

        var statement = Json(await Queued());
        Assert.Equal("Unknown Application", statement["object"]["definition"]["name"]["en-US"].GetValue<string>());
        Assert.Equal($"{ApiUrl}/views/{view.Id}/apps/unknown", statement["object"]["id"].GetValue<string>());
    }

    /// <summary>
    /// Pins that <c>applicationUrl</c> is accepted and ignored — the statement is identical whatever is
    /// passed. Issue 28: either the URL belongs in the statement or the parameter should go.
    /// </summary>
    [Fact]
    public async Task EmitApplicationSwitched_ignores_the_application_url()
    {
        var view = TestData.View();
        await Seed(view);
        var service = Service(Caller());

        await service.EmitApplicationSwitchedAsync(view.Id, "vm", "https://first.test", Ct);
        await service.EmitApplicationSwitchedAsync(view.Id, "vm", "https://second.test", Ct);

        var statements = await Db.XApiQueuedStatements.Select(x => x.StatementJson).ToListAsync(Ct);
        Assert.Equal(2, statements.Count);
        Assert.Single(statements.Distinct());
        Assert.DoesNotContain("first.test", statements[0]);
    }

    /// <summary>
    /// The view is not looked up, so an app switch is reported even for a view that has been deleted.
    /// </summary>
    [Fact]
    public async Task EmitApplicationSwitched_queues_a_statement_for_a_view_that_does_not_exist()
    {
        await Service(Caller()).EmitApplicationSwitchedAsync(Guid.NewGuid(), "vm", null, Ct);

        Assert.Equal("accessed", (await Queued()).Verb);
    }

    // ---- Joining a team ---------------------------------------------------------------------------

    [Fact]
    public async Task EmitTeamJoined_queues_an_attended_statement_naming_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        await Service(Caller()).EmitTeamJoinedAsync(team.Id, view.Id, Ct);

        var queued = await Queued();
        Assert.Equal("attended", queued.Verb);
        Assert.Equal($"{ApiUrl}/teams/{team.Id}", queued.ActivityId);
        Assert.Equal(view.Id, queued.ViewId);

        var statement = Json(queued);
        Assert.Equal("http://adlnet.gov/expapi/verbs/attended", statement["verb"]["id"].GetValue<string>());
        Assert.Equal("attended", statement["verb"]["display"]["en-US"].GetValue<string>());
        Assert.Equal("http://id.tincanapi.com/activitytype/team", statement["object"]["definition"]["type"].GetValue<string>());
        Assert.Equal("Blue Team", statement["object"]["definition"]["name"]["en-US"].GetValue<string>());
        Assert.Equal(view.Id.ToString(), statement["context"]["registration"].GetValue<string>());
    }

    /// <summary>
    /// The statement names the team, so without one there is nothing to name.
    /// </summary>
    [Fact]
    public async Task EmitTeamJoined_queues_nothing_for_a_team_that_does_not_exist()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitTeamJoinedAsync(Guid.NewGuid(), view.Id, Ct);

        Assert.Empty(await Db.XApiQueuedStatements.ToListAsync(Ct));
    }

    /// <summary>
    /// Unlike every other emit, this one builds its context without the team, so the team appears only
    /// as the statement's object and not as the team extension a consumer filters on.
    /// </summary>
    [Fact]
    public async Task EmitTeamJoined_omits_the_team_context_extension()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Service(Caller()).EmitTeamJoinedAsync(team.Id, view.Id, Ct);

        Assert.Null(TeamExtension(Json(await Queued())));
    }

    /// <summary>
    /// A team from another view is reported against the view id it was given: the two arguments are not
    /// cross-checked.
    /// </summary>
    [Fact]
    public async Task EmitTeamJoined_does_not_check_the_team_belongs_to_the_view()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var team = TestData.Team(other.Id);
        await Seed(view, other, team);

        await Service(Caller()).EmitTeamJoinedAsync(team.Id, view.Id, Ct);

        Assert.Equal(view.Id, (await Queued()).ViewId);
    }

    // ---- Switching teams --------------------------------------------------------------------------

    /// <summary>
    /// The team switched to is both the object and the context team, and the view is the parent — so a
    /// consumer can read the move without resolving the team separately.
    /// </summary>
    [Fact]
    public async Task EmitTeamSwitched_queues_a_switched_statement_naming_the_team_and_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Red Team");
        await Seed(view, team);

        await Service(Caller()).EmitTeamSwitchedAsync(view.Id, team.Id, Ct);

        var queued = await Queued();
        Assert.Equal("switched", queued.Verb);
        Assert.Equal($"{ApiUrl}/teams/{team.Id}", queued.ActivityId);
        Assert.Equal(view.Id, queued.ViewId);

        var statement = Json(queued);
        Assert.Equal("https://w3id.org/xapi/verbs/switched", statement["verb"]["id"].GetValue<string>());
        Assert.Equal("switched", statement["verb"]["display"]["en-US"].GetValue<string>());
        Assert.Equal("Red Team", statement["object"]["definition"]["name"]["en-US"].GetValue<string>());
        Assert.Equal(team.Id.ToString(), TeamExtension(statement));

        var parent = Assert.Single(statement["context"]["contextActivities"]["parent"].AsArray());
        Assert.Equal($"{ApiUrl}/views/{view.Id}", parent["id"].GetValue<string>());
    }

    /// <summary>
    /// The extension is the team switched to, not the caller's current primary team, so the statement
    /// records the destination of the move.
    /// </summary>
    [Fact]
    public async Task EmitTeamSwitched_reports_the_target_team_rather_than_the_primary_team()
    {
        var view = TestData.View();
        var primary = TestData.Team(view.Id, "Primary");
        var target = TestData.Team(view.Id, "Target");
        var user = TestData.User();
        await Seed(view, primary, target, user);
        await MakeMember(view, primary, user);

        await Service(Caller(user.Id)).EmitTeamSwitchedAsync(view.Id, target.Id, Ct);

        Assert.Equal(target.Id.ToString(), TeamExtension(Json(await Queued())));
    }

    [Fact]
    public async Task EmitTeamSwitched_queues_nothing_for_a_team_that_does_not_exist()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitTeamSwitchedAsync(view.Id, Guid.NewGuid(), Ct);

        Assert.Empty(await Db.XApiQueuedStatements.ToListAsync(Ct));
    }

    /// <summary>
    /// A team with no name still yields a statement — the name is a label on the activity, not a key.
    /// </summary>
    [Fact]
    public async Task EmitTeamSwitched_substitutes_a_placeholder_for_a_team_with_no_name()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, name: null);
        await Seed(view, team);

        await Service(Caller()).EmitTeamSwitchedAsync(view.Id, team.Id, Ct);

        Assert.Equal("Unnamed Team", Json(await Queued())["object"]["definition"]["name"]["en-US"].GetValue<string>());
    }

    // ---- Terminating a view -----------------------------------------------------------------------

    /// <summary>
    /// The duration is the reason this statement exists — it is how time-on-task is reported — and it
    /// goes out as an ISO 8601 period.
    /// </summary>
    [Fact]
    public async Task EmitViewTerminated_queues_a_terminated_statement_carrying_the_duration()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitViewTerminatedAsync(view.Id, new TimeSpan(1, 30, 0), Ct);

        var queued = await Queued();
        Assert.Equal("terminated", queued.Verb);
        Assert.Equal($"{ApiUrl}/views/{view.Id}", queued.ActivityId);

        var statement = Json(queued);
        Assert.Equal("http://adlnet.gov/expapi/verbs/terminated", statement["verb"]["id"].GetValue<string>());
        Assert.Equal("terminated", statement["verb"]["display"]["en-US"].GetValue<string>());
        Assert.Equal("PT1H30M", statement["result"]["duration"].GetValue<string>());
    }

    /// <summary>
    /// Unlike <c>EmitViewViewed</c>, the view is not looked up — which is what makes this usable from
    /// the path that reports a session ending after the view row is gone.
    /// </summary>
    [Fact]
    public async Task EmitViewTerminated_queues_a_statement_for_a_view_that_does_not_exist()
    {
        var viewId = Guid.NewGuid();

        await Service(Caller()).EmitViewTerminatedAsync(viewId, TimeSpan.Zero, Ct);

        var queued = await Queued();
        Assert.Equal("terminated", queued.Verb);
        Assert.Equal(viewId, queued.ViewId);
        Assert.Null(Json(queued)["object"]["definition"]["name"]);
    }

    [Fact]
    public async Task EmitViewTerminated_carries_the_callers_primary_team_as_a_context_extension()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);
        await MakeMember(view, team, user);

        await Service(Caller(user.Id)).EmitViewTerminatedAsync(view.Id, TimeSpan.FromMinutes(5), Ct);

        Assert.Equal(team.Id.ToString(), TeamExtension(Json(await Queued())));
    }

    // ---- Queue metadata --------------------------------------------------------------------------

    /// <summary>
    /// Statements are handed to the queue pending and unattempted, which is the state the background
    /// sender selects on; a statement queued in any other state would never be sent.
    /// </summary>
    [Fact]
    public async Task Queued_statements_start_pending_with_no_attempts()
    {
        var view = TestData.View();
        await Seed(view);

        await Service(Caller()).EmitViewViewedAsync(view.Id, Ct);

        var queued = await Queued();
        Assert.Equal(XApiQueueStatus.Pending, queued.Status);
        Assert.Equal(0, queued.RetryCount);
        Assert.Null(queued.LastAttemptAt);
        Assert.Null(queued.ErrorMessage);
        Assert.Equal(DateTimeKind.Utc, queued.QueuedAt.Kind);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// A service with xAPI switched on. <paramref name="configure"/> overrides the defaults, and is
    /// honored only because each test passes a principal no other host was built for.
    /// </summary>
    private IXApiService Service(ClaimsPrincipal caller, Action<XApiOptions> configure = null) =>
        HostFor(caller, o =>
        {
            o.XApi.Enabled = true;
            o.XApi.Username = "lrs-user";
            o.XApi.ApiUrl = ApiUrl;
            o.XApi.Platform = "Player";
            configure?.Invoke(o.XApi);
        }).Resolve<IXApiService>();

    /// <summary>
    /// A caller carrying the <c>iss</c> claim the actor is built from. A null
    /// <paramref name="issuer"/> omits it, which is the shape issue 27 is about.
    /// </summary>
    private static ClaimsPrincipal Caller(Guid? userId = null, string issuer = Issuer)
    {
        var builder = new ClaimsPrincipalBuilder().WithUserId(userId ?? Guid.NewGuid());

        if (issuer != null)
        {
            builder.WithClaim("iss", issuer);
        }

        return builder.Build();
    }

    /// <summary>Calls every entry point, for the tests about paths that emit nothing at all.</summary>
    private static async Task EmitEverything(IXApiService service, Guid viewId, Guid teamId)
    {
        await service.EmitViewViewedAsync(viewId, Ct);
        await service.EmitApplicationSwitchedAsync(viewId, "vm", "https://vm.test", Ct);
        await service.EmitTeamJoinedAsync(teamId, viewId, Ct);
        await service.EmitTeamSwitchedAsync(viewId, teamId, Ct);
        await service.EmitViewTerminatedAsync(viewId, TimeSpan.FromMinutes(1), Ct);
    }

    /// <summary>
    /// Makes <paramref name="user"/> a member of <paramref name="view"/> with
    /// <paramref name="team"/> as their primary. The primary is assigned after the insert because the
    /// two rows reference each other.
    /// </summary>
    private async Task MakeMember(ViewEntity view, TeamEntity team, UserEntity user)
    {
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(viewMembership, teamMembership);

        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);
    }

    /// <summary>The one queued statement, asserting that exactly one was queued.</summary>
    private async Task<XApiQueuedStatementEntity> Queued() =>
        Assert.Single(await Db.XApiQueuedStatements.ToListAsync(Ct));

    private static JsonNode Json(XApiQueuedStatementEntity statement) =>
        JsonNode.Parse(statement.StatementJson);

    private static string HomePage(JsonNode statement) =>
        statement["actor"]["account"]["homePage"].GetValue<string>();

    /// <summary>The team the statement was made in, or null when it carries no team.</summary>
    private static string TeamExtension(JsonNode statement) =>
        statement["context"]["extensions"]?[TeamExtensionKey]?.GetValue<string>();
}
