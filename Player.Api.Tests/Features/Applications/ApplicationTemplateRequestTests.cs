// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Infrastructure.Constants;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// Covers application templates — the view-independent originals applications inherit from — and the
/// archive export and import that move them between installations, over HTTP.
/// </summary>
public class ApplicationTemplateRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    private static readonly byte[] IconBytes = [0x89, 0x50, 0x4E, 0x47];

    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task CreateApplicationTemplate_persists_the_template()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/application-templates",
            new
            {
                name = "Console",
                url = "https://example.test/console",
                icon = "https://example.test/console.png",
                embeddable = true,
                loadInBackground = true
            },
            Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<ApplicationTemplate>(response);

        Assert.Equal("Console", created.Name);
        Assert.True(created.Embeddable);
        Assert.True(created.LoadInBackground);

        // CreatedAtRoute names the route "GetApplicationTemplate" while the endpoint is registered as
        // "getApplicationTemplate", so this also pins that the lookup is case-insensitive — an ordinal
        // one would answer 500 instead.
        Assert.Equal(
            $"/api/application-templates/{created.Id}",
            response.Headers.Location?.AbsolutePath);

        // The stored row rather than the response, since the response is mapped from the entity the
        // handler holds in memory and would read the same whether or not the save took the values with it.
        await using var db = NewContext();
        var stored = await db.ApplicationTemplates.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Console", stored.Name);
        Assert.True(stored.Embeddable);
        Assert.True(stored.LoadInBackground);
    }

    /// <summary>
    /// Templates are not scoped to a view, so only the system permission opens them — being a view
    /// administrator does not. The refusal arrives as a 500 rather than the 403 every other refusal here
    /// answers with.
    /// </summary>
    /// <remarks>
    /// Characterizes current behaviour. The <c>Authorize(SystemPermission[], CancellationToken)</c>
    /// overload passes null for the view and team permission arrays
    /// (<c>AuthorizationService.cs:55</c>), and <c>TeamPermissionsHandler</c> enumerates them for any
    /// caller who holds a team permissions claim (<c>TeamPermissionRequirement.cs:70</c>) — so a member
    /// of any team gets an <see cref="ArgumentNullException"/> where a non-member gets a clean 403. Turns
    /// red once the null arrays are handled and this answers 403 too.
    /// </remarks>
    [Fact]
    public async Task CreateApplicationTemplate_is_refused_for_a_caller_holding_only_ManageView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await Client(actor).PostAsJsonAsync(
                "api/application-templates", new { name = "Nope" }, Ct));

        Assert.Equal("Value cannot be null. (Parameter 'source')", problem.Detail);
    }

    [Fact]
    public async Task EditApplicationTemplate_updates_the_template()
    {
        var template = TestData.ApplicationTemplate("Before", embeddable: false);
        await Seed(template);

        var edited = await ReadAsync<ApplicationTemplate>(await RootClient.PutAsJsonAsync(
            $"api/application-templates/{template.Id}",
            new { name = "After", url = "https://example.test/after", embeddable = true },
            Ct));

        Assert.Equal("After", edited.Name);
        Assert.Equal("https://example.test/after", edited.Url);
        Assert.True(edited.Embeddable);
    }

    [Fact]
    public async Task EditApplicationTemplate_reports_a_missing_template_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/application-templates/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    [Fact]
    public async Task EditApplicationTemplate_is_forbidden_for_a_caller_with_no_permissions()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PutAsJsonAsync(
            $"api/application-templates/{template.Id}", new { name = "Nope" }, Ct));
    }

    [Fact]
    public async Task DeleteApplicationTemplate_removes_the_template()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/application-templates/{template.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.ApplicationTemplates.AnyAsync(Ct));
    }

    /// <summary>
    /// Characterizes a live 500, and it is wrong: a template any application was built from cannot be
    /// deleted at all. The applications were meant to survive with their template id cleared; instead
    /// the foreign key refuses the delete and nothing changes.
    /// </summary>
    /// <remarks>
    /// <c>ApplicationEntity.ApplicationTemplateId</c> is an optional foreign key with no <c>OnDelete</c>
    /// configured (<c>Application.cs:49</c>, and <c>PlayerContext</c> configures no relationship for it),
    /// so EF's default is <c>ClientSetNull</c>: the store constraint is <c>NO ACTION</c> and EF clears
    /// only the children it already tracks. The handler loads the template alone
    /// (<c>DeleteApplicationTemplate.cs:57</c>), so there is nothing to fix up and
    /// <c>SaveChangesAsync</c> hits the constraint. Turns red when the relationship is configured
    /// <c>SetNull</c>, or when the handler loads the applications it orphans.
    /// </remarks>
    [Fact]
    public async Task DeleteApplicationTemplate_is_refused_while_an_application_uses_it()
    {
        var view = TestData.View();
        var template = TestData.ApplicationTemplate();
        var application = TestData.Application(view.Id, "Kept", templateId: template.Id);
        await Seed(view, template, application);

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.DeleteAsync($"api/application-templates/{template.Id}", Ct));

        Assert.Equal(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            problem.Detail);

        await using var db = NewContext();
        Assert.True(await db.ApplicationTemplates.AnyAsync(x => x.Id == template.Id, Ct));

        var kept = await db.Applications.SingleAsync(Ct);
        Assert.Equal("Kept", kept.Name);
        Assert.Equal(template.Id, kept.ApplicationTemplateId);
    }

    [Fact]
    public async Task DeleteApplicationTemplate_reports_a_missing_template_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/application-templates/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task DeleteApplicationTemplate_is_forbidden_for_a_caller_with_no_permissions()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/application-templates/{template.Id}", Ct));
    }

    // ---- Reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetApplicationTemplate_returns_the_template()
    {
        var template = TestData.ApplicationTemplate("Findable");
        await Seed(template);

        var got = await ReadAsync<ApplicationTemplate>(
            await RootClient.GetAsync($"api/application-templates/{template.Id}", Ct));

        Assert.Equal("Findable", got.Name);
    }

    /// <summary>
    /// Characterizes current behaviour, and it is wrong: a template id with no row should be a 404. It
    /// is answered 200 with no <c>Content-Type</c> and a zero-byte body, so a client cannot tell an
    /// unknown template from one that exists.
    /// </summary>
    /// <remarks>
    /// The handler reads with <c>SingleOrDefaultAsync</c> and does not check the result
    /// (<c>GetApplicationTemplate.cs:63</c>), and <c>Ok&lt;TValue&gt;</c> writes nothing at all for a
    /// null value. The inconsistency is inside this one feature: <c>EditApplicationTemplate.cs:66</c>
    /// and <c>DeleteApplicationTemplate.cs:61</c> both throw
    /// <c>EntityNotFoundException&lt;ApplicationTemplate&gt;</c> for the same condition. Turns red when
    /// the read does the same. Needs a privileged caller: <c>ViewViews</c> short-circuits the
    /// authorization check before the resource is loaded, so a caller without it gets 403 here instead.
    /// </remarks>
    [Fact]
    public async Task GetApplicationTemplate_returns_nothing_for_a_missing_template()
    {
        var response = await RootClient.GetAsync($"api/application-templates/{Guid.NewGuid()}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    /// <summary>
    /// Not scoped to any view, but <c>ManageView</c> anywhere qualifies — a view administrator needs to
    /// read templates to add applications from them.
    /// </summary>
    [Fact]
    public async Task GetApplicationTemplate_is_allowed_for_a_view_administrator()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        var template = TestData.ApplicationTemplate();
        await Seed(view, role, team, template);

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        var got = await ReadAsync<ApplicationTemplate>(
            await Client(actor).GetAsync($"api/application-templates/{template.Id}", Ct));

        Assert.Equal(template.Id, got.Id);
    }

    [Fact]
    public async Task GetApplicationTemplate_is_forbidden_for_a_caller_with_no_permissions()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/application-templates/{template.Id}", Ct));
    }

    [Fact]
    public async Task GetAllTemplates_returns_every_template()
    {
        await Seed(TestData.ApplicationTemplate("First"), TestData.ApplicationTemplate("Second"));

        var templates = await ReadAsync<ApplicationTemplate[]>(
            await RootClient.GetAsync("api/application-templates", Ct));

        Assert.Equal(["First", "Second"], templates.Select(x => x.Name).Order());
    }

    [Fact]
    public async Task GetAllTemplates_is_allowed_for_a_view_administrator()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team, TestData.ApplicationTemplate());

        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ManageView]).SeedAsync();

        Assert.Single(await ReadAsync<ApplicationTemplate[]>(
            await Client(actor).GetAsync("api/application-templates", Ct)));
    }

    [Fact]
    public async Task GetAllTemplates_is_forbidden_for_a_caller_with_no_permissions()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync("api/application-templates", Ct));
    }

    // ---- Export ---------------------------------------------------------------------------------

    [Fact]
    public async Task ExportApplicationTemplates_packs_the_requested_templates_only()
    {
        var wanted = TestData.ApplicationTemplate("Wanted");
        await Seed(wanted, TestData.ApplicationTemplate("Unwanted"));

        var response = await Export(ids: [wanted.Id]);

        Assert.Equal("Wanted", Assert.Single(await ReadTemplates(response)).Name);
        Assert.False(HasArchiveErrors(response));
    }

    /// <summary>
    /// No ids means everything, which is how a whole installation's templates are exported at once.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_packs_every_template_when_no_ids_are_given()
    {
        await Seed(TestData.ApplicationTemplate("First"), TestData.ApplicationTemplate("Second"));

        Assert.Equal(
            ["First", "Second"],
            (await ReadTemplates(await Export())).Select(x => x.Name).Order());
    }

    [Fact]
    public async Task ExportApplicationTemplates_names_the_archive_for_the_requested_type()
    {
        await Seed(TestData.ApplicationTemplate());

        var response = await Export(ArchiveType.tgz);

        Assert.Equal($"{ApplicationConstants.ExportFileName}.tar.gz", ArchiveName(response));
        Assert.Single(await ReadTemplates(response));
    }

    /// <summary>
    /// With <c>includeIcons</c> false nothing is fetched, even for an icon the stub would have served:
    /// the json keeps the icon urls as they are, and the archive holds only the json.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_does_not_fetch_icons_unless_asked()
    {
        var icon = IconUrl();
        await Seed(TestData.ApplicationTemplate(icon: icon));
        var icons = Icons().Respond(icon, IconBytes);

        var response = await Export();

        Assert.DoesNotContain(icon, icons.Requests);
        Assert.Equal(icon, Assert.Single(await ReadTemplates(response)).Icon);
        Assert.Equal([ApplicationConstants.ExportFileName], (await Files(response)).Keys);
    }

    /// <summary>
    /// The fetched bytes travel beside the json, so an import can restore icons that were only reachable
    /// from the exporting installation.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_packs_fetched_icons_beside_the_json()
    {
        var icon = IconUrl();
        await Seed(TestData.ApplicationTemplate(icon: icon));
        var icons = Icons().Respond(icon, IconBytes);

        var response = await Export(includeIcons: true);

        Assert.Contains(icon, icons.Requests);
        Assert.False(HasArchiveErrors(response));

        var packed = Assert.Single(
            await Files(response), x => x.Key != ApplicationConstants.ExportFileName);
        Assert.Equal(IconBytes, packed.Value);

        // The url is left alone — the packed bytes are a companion, not a replacement.
        Assert.Equal(icon, Assert.Single(await ReadTemplates(response)).Icon);
    }

    /// <summary>
    /// Embedding inlines each icon as a data uri instead, leaving a single self-contained json file.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_inlines_icons_when_embedding()
    {
        var icon = IconUrl();
        await Seed(TestData.ApplicationTemplate(icon: icon));
        Icons().Respond(icon, IconBytes);

        var response = await Export(includeIcons: true, embedIcons: true);

        Assert.Equal([ApplicationConstants.ExportFileName], (await Files(response)).Keys);
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(IconBytes)}",
            Assert.Single(await ReadTemplates(response)).Icon);
    }

    /// <summary>
    /// A response with no content type falls back to the media type the icon's extension implies.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_infers_the_media_type_from_the_icon_extension()
    {
        var icon = IconUrl("svg");
        await Seed(TestData.ApplicationTemplate(icon: icon));
        Icons().Respond(icon, IconBytes, contentType: null);

        var response = await Export(includeIcons: true, embedIcons: true);

        Assert.StartsWith(
            "data:image/svg+xml;base64,", Assert.Single(await ReadTemplates(response)).Icon);
    }

    /// <summary>
    /// A failed fetch does not fail the export: the templates still come back, the reason is packed as a
    /// text file, and the response header is what tells the caller to look.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_records_a_failed_icon_fetch_as_an_error()
    {
        var icon = IconUrl();
        await Seed(TestData.ApplicationTemplate("Broken", icon: icon));
        Icons().RespondWithStatus(icon, HttpStatusCode.NotFound);

        var response = await Export(includeIcons: true);

        Assert.True(HasArchiveErrors(response));
        Assert.Single(await ReadTemplates(response));

        var errors = Encoding.UTF8.GetString(
            (await Files(response))[ApplicationConstants.ErrorFileName]);
        Assert.Contains("Name: Broken", errors);
        Assert.Contains($"Icon: {icon}", errors);
    }

    [Fact]
    public async Task ExportApplicationTemplates_records_a_thrown_icon_fetch_as_an_error()
    {
        var icon = IconUrl();
        await Seed(TestData.ApplicationTemplate("Unreachable", icon: icon));
        Icons().RespondByThrowing(icon, new HttpRequestException("no route"));

        var response = await Export(includeIcons: true);

        Assert.True(HasArchiveErrors(response));
        Assert.Contains(
            "Error: no route",
            Encoding.UTF8.GetString((await Files(response))[ApplicationConstants.ErrorFileName]));
    }

    /// <summary>
    /// An icon that is not an absolute http url is skipped rather than reported — a template with no
    /// icon, or a relative one, is not an error.
    /// </summary>
    /// <remarks>
    /// Nothing is registered on the stub, so a fetch this test says cannot happen would answer 404 and
    /// show up as a packed error file rather than as a failure to reach the network.
    /// </remarks>
    [Fact]
    public async Task ExportApplicationTemplates_skips_an_icon_that_is_not_a_url()
    {
        await Seed(
            TestData.ApplicationTemplate("No icon", icon: null),
            TestData.ApplicationTemplate("Relative icon", icon: "/assets/icon.png"));

        var response = await Export(includeIcons: true);

        Assert.False(HasArchiveErrors(response));
        Assert.Equal([ApplicationConstants.ExportFileName], (await Files(response)).Keys);
    }

    /// <summary>
    /// Characterizes a live 500: this export serializes with <c>UnsafeRelaxedJsonEscaping</c>
    /// (<c>ExportApplicationTemplates.cs:147</c>) so non-ASCII reaches the archive raw, and
    /// <c>ArchiveService.cs:71</c> declares the tar entry's size as a char count. One accented character
    /// in a template name is enough to make every tgz export of it fail. See issue 42.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_as_tgz_fails_when_a_template_name_is_not_ascii()
    {
        await Seed(TestData.ApplicationTemplate("Übung"));

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.GetAsync(ExportRoute(ArchiveType.tgz), Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Contains("bytes", problem.Detail);
    }

    /// <summary>Zip never declares the size, so the same template exports and reads back intact.</summary>
    [Fact]
    public async Task ExportApplicationTemplates_as_zip_keeps_a_name_that_is_not_ascii()
    {
        await Seed(TestData.ApplicationTemplate("Übung"));

        Assert.Equal("Übung", Assert.Single(await ReadTemplates(await Export())).Name);
    }

    [Fact]
    public async Task ExportApplicationTemplates_is_forbidden_for_a_caller_with_no_permissions()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync(ExportRoute(), Ct));
    }

    // ---- Import ---------------------------------------------------------------------------------

    /// <summary>
    /// An export from one installation restores into another, ids and all — which is what makes the
    /// archive a transfer format rather than a backup.
    /// </summary>
    [Fact]
    public async Task ImportApplicationTemplates_inserts_a_template_that_is_not_present()
    {
        var template = TestData.ApplicationTemplate("Portable", "https://example.test/portable");
        await Seed(template);

        var exported = await Export();

        // Stand in for the receiving installation, which does not have it yet.
        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/application-templates/{template.Id}", Ct));

        var result = await Import(exported);

        Assert.Empty(result.Failures);

        await using var db = NewContext();
        var imported = await db.ApplicationTemplates.SingleAsync(Ct);
        Assert.Equal(template.Id, imported.Id);
        Assert.Equal("Portable", imported.Name);
        Assert.Equal("https://example.test/portable", imported.Url);
    }

    /// <summary>
    /// An id that is already present is reported rather than replaced, so an import cannot quietly
    /// overwrite a template other views depend on.
    /// </summary>
    [Fact]
    public async Task ImportApplicationTemplates_reports_an_existing_template_as_a_failure()
    {
        var template = TestData.ApplicationTemplate("Existing");
        await Seed(template);

        var exported = await Export();
        await Rename(template.Id, "Changed");

        var result = await Import(exported);

        Assert.Equal([$"Existing ({template.Id})"], result.Failures);

        await using var db = NewContext();
        Assert.Equal("Changed", (await db.ApplicationTemplates.SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task ImportApplicationTemplates_overwrites_an_existing_template_when_asked()
    {
        var template = TestData.ApplicationTemplate("Original");
        await Seed(template);

        var exported = await Export();
        await Rename(template.Id, "Changed");

        var result = await Import(exported, overWriteExisting: true);

        Assert.Empty(result.Failures);

        await using var db = NewContext();
        Assert.Equal("Original", (await db.ApplicationTemplates.SingleAsync(Ct)).Name);
    }

    /// <summary>
    /// The tgz half of the round trip: the extension on the uploaded file name is what selects the
    /// reader, so an archive exported as tgz imports as tgz.
    /// </summary>
    [Fact]
    public async Task ImportApplicationTemplates_reads_a_tgz_archive()
    {
        var template = TestData.ApplicationTemplate("Tarred");
        await Seed(template);

        var exported = await Export(ArchiveType.tgz);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/application-templates/{template.Id}", Ct));

        var result = await Import(exported);

        Assert.Empty(result.Failures);

        await using var db = NewContext();
        Assert.Equal("Tarred", (await db.ApplicationTemplates.SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task ImportApplicationTemplates_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Seed(TestData.ApplicationTemplate());
        var exported = await Export();

        var actor = await Actor().SeedAsync();

        using var upload = ArchiveHelper.AsUpload(
            await exported.Content.ReadAsByteArrayAsync(Ct), ArchiveName(exported));

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).PostAsync(ImportRoute(), upload, Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// The export route, with the three flags binding makes mandatory. A request omitting any of them is
    /// answered with a bare 400 and an empty body before the handler is reached — see issue 49.
    /// </summary>
    private static string ExportRoute(
        ArchiveType archiveType = ArchiveType.zip,
        bool includeIcons = false,
        bool embedIcons = false,
        Guid[] ids = null)
    {
        var route = new StringBuilder("api/application-templates/actions/export")
            .Append($"?archiveType={archiveType}")
            .Append($"&includeIcons={(includeIcons ? "true" : "false")}")
            .Append($"&embedIcons={(embedIcons ? "true" : "false")}");

        foreach (var id in ids ?? [])
        {
            route.Append($"&ids={id}");
        }

        return route.ToString();
    }

    /// <summary>The import route, with the flag binding makes mandatory for the same reason.</summary>
    private static string ImportRoute(bool overWriteExisting = false) =>
        "api/application-templates/actions/import" +
        $"?overWriteExisting={(overWriteExisting ? "true" : "false")}";

    /// <summary>An export as <c>Root</c>, for the tests that read the archive back.</summary>
    private async Task<HttpResponseMessage> Export(
        ArchiveType archiveType = ArchiveType.zip,
        bool includeIcons = false,
        bool embedIcons = false,
        Guid[] ids = null)
    {
        var response = await RootClient.GetAsync(
            ExportRoute(archiveType, includeIcons, embedIcons, ids), Ct);

        await AssertStatus(HttpStatusCode.OK, response);

        return response;
    }

    /// <summary>Uploads the archive an export answered with, under the file part the importer binds.</summary>
    private async Task<ImportApplicationTemplates.ImportApplicationTemplatesResult> Import(
        HttpResponseMessage exported,
        bool overWriteExisting = false)
    {
        using var upload = ArchiveHelper.AsUpload(
            await exported.Content.ReadAsByteArrayAsync(Ct), ArchiveName(exported));

        return await ReadAsync<ImportApplicationTemplates.ImportApplicationTemplatesResult>(
            await RootClient.PostAsync(ImportRoute(overWriteExisting), upload, Ct));
    }

    /// <summary>
    /// Renames a template over HTTP, for the import tests that need the stored row to differ from the
    /// archive they exported.
    /// </summary>
    private async Task Rename(Guid id, string name) =>
        await ReadAsync<ApplicationTemplate>(await RootClient.PutAsJsonAsync(
            $"api/application-templates/{id}", new { name }, Ct));

    /// <summary>
    /// The stub the export handler's outbound client already answers from, for arranging icons on.
    /// </summary>
    /// <remarks>
    /// One stub serves the whole run, so a registration outlives the test that made it and keeps
    /// answering afterwards. Each test therefore registers a url of its own (<see cref="IconUrl"/>) and
    /// asserts on that url alone — an unregistered url is answered 404, and nothing here counts calls.
    /// </remarks>
    private StubHttpMessageHandler Icons() => Factory.OutboundHttp;

    /// <summary>
    /// An icon url no other test uses, so an assertion on what was fetched cannot see another test's
    /// request through the shared factory.
    /// </summary>
    private static string IconUrl(string extension = "png") =>
        $"https://example.test/{Guid.NewGuid():N}.{extension}";

    /// <summary>The entries of the archive a response carried, by name.</summary>
    private static async Task<Dictionary<string, byte[]>> Files(HttpResponseMessage response) =>
        ArchiveHelper.ExtractFiles(
            await response.Content.ReadAsByteArrayAsync(Ct), ArchiveName(response));

    private static async Task<ApplicationTemplate[]> ReadTemplates(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<ApplicationTemplate[]>(
            (await Files(response))[ApplicationConstants.ExportFileName]);

    /// <summary>
    /// The archive's file name, which the extractor and the importer both read the archive type from.
    /// </summary>
    private static string ArchiveName(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    /// <summary>
    /// Whether the response carries the archive-errors marker. Both collections are checked because the
    /// assertion is sometimes a negative one, and a header looked for in the wrong place is absent from it.
    /// </summary>
    private static bool HasArchiveErrors(HttpResponseMessage response) =>
        response.Headers.Contains(HttpConstants.ArchiveErrorsHeader) ||
        response.Content.Headers.Contains(HttpConstants.ArchiveErrorsHeader);
}
