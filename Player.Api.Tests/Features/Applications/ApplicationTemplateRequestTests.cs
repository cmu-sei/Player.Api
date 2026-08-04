// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Models;
using Player.Api.Features.Applications;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Applications;

/// <summary>
/// Covers application templates — the view-independent originals applications inherit from — and the
/// archive export and import that move them between installations.
/// </summary>
public class ApplicationTemplateRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    private static readonly byte[] IconBytes = [0x89, 0x50, 0x4E, 0x47];

    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task CreateApplicationTemplate_persists_the_template()
    {
        var created = await SendAsync(new CreateApplicationTemplate.Command
        {
            Name = "Console",
            Url = "https://example.test/console",
            Icon = "https://example.test/console.png",
            Embeddable = true,
            LoadInBackground = true
        });

        Assert.Equal("Console", created.Name);
        Assert.True(created.Embeddable);
        Assert.True(created.LoadInBackground);

        await using var db = NewContext();
        Assert.True(await db.ApplicationTemplates.AnyAsync(x => x.Id == created.Id, Ct));
    }

    /// <summary>
    /// Templates are not scoped to a view, so only the system permission opens them — being a view
    /// administrator does not.
    /// </summary>
    /// <remarks>
    /// Characterizes current behaviour. The refusal arrives as
    /// <see cref="ArgumentNullException"/> rather than <see cref="ForbiddenException"/>: the
    /// <c>Authorize(SystemPermission[], CancellationToken)</c> overload passes null for the view and team
    /// permission arrays, and the requirement handler enumerates them for any caller who holds a team
    /// permissions claim. Flip to <see cref="ForbiddenException"/> once the null arrays are handled.
    /// </remarks>
    [Fact]
    public async Task CreateApplicationTemplate_is_refused_for_a_caller_holding_only_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SendAsync(caller, new CreateApplicationTemplate.Command { Name = "Nope" }));
    }

    [Fact]
    public async Task EditApplicationTemplate_updates_the_template()
    {
        var template = TestData.ApplicationTemplate("Before", embeddable: false);
        await Seed(template);

        var edited = await SendAsync(new EditApplicationTemplate.Command
        {
            Id = template.Id,
            Name = "After",
            Url = "https://example.test/after",
            Embeddable = true
        });

        Assert.Equal("After", edited.Name);
        Assert.Equal("https://example.test/after", edited.Url);
        Assert.True(edited.Embeddable);
    }

    [Fact]
    public async Task EditApplicationTemplate_reports_a_missing_template_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<ApplicationTemplate>>(
            () => SendAsync(new EditApplicationTemplate.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task EditApplicationTemplate_is_forbidden_for_a_caller_with_no_permissions()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new EditApplicationTemplate.Command { Id = template.Id, Name = "Nope" }));
    }

    [Fact]
    public async Task DeleteApplicationTemplate_removes_the_template()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        await SendAsync(new DeleteApplicationTemplate.Command { Id = template.Id });

        await using var db = NewContext();
        Assert.False(await db.ApplicationTemplates.AnyAsync(Ct));
    }

    /// <summary>
    /// The applications built from it survive, keeping whatever they set themselves.
    /// </summary>
    [Fact]
    public async Task DeleteApplicationTemplate_leaves_the_applications_that_used_it()
    {
        var view = TestData.View();
        var template = TestData.ApplicationTemplate();
        var application = TestData.Application(view.Id, "Kept", templateId: template.Id);
        await Seed(view, template, application);

        await SendAsync(new DeleteApplicationTemplate.Command { Id = template.Id });

        await using var db = NewContext();
        var kept = await db.Applications.SingleAsync(Ct);
        Assert.Equal("Kept", kept.Name);
        Assert.Null(kept.ApplicationTemplateId);
    }

    [Fact]
    public async Task DeleteApplicationTemplate_reports_a_missing_template_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<ApplicationTemplate>>(
            () => SendAsync(new DeleteApplicationTemplate.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task DeleteApplicationTemplate_is_forbidden_for_a_caller_with_no_permissions()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new DeleteApplicationTemplate.Command { Id = template.Id }));
    }

    // ---- Reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetApplicationTemplate_returns_the_template()
    {
        var template = TestData.ApplicationTemplate("Findable");
        await Seed(template);

        Assert.Equal(
            "Findable",
            (await SendAsync(new GetApplicationTemplate.Query { Id = template.Id })).Name);
    }

    /// <summary>
    /// Characterizes current behaviour: a missing id returns null rather than throwing.
    /// </summary>
    [Fact]
    public async Task GetApplicationTemplate_returns_nothing_for_a_missing_template()
    {
        Assert.Null(await SendAsync(new GetApplicationTemplate.Query { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// Not scoped to any view, but <c>ManageView</c> anywhere qualifies — a view administrator needs to
    /// read templates to add applications from them.
    /// </summary>
    [Fact]
    public async Task GetApplicationTemplate_is_allowed_for_a_view_administrator()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var template = TestData.ApplicationTemplate();
        await Seed(view, team, template);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        Assert.Equal(
            template.Id,
            (await SendAsync(caller, new GetApplicationTemplate.Query { Id = template.Id })).Id);
    }

    [Fact]
    public async Task GetApplicationTemplate_is_forbidden_for_a_caller_with_no_permissions()
    {
        var template = TestData.ApplicationTemplate();
        await Seed(template);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetApplicationTemplate.Query { Id = template.Id }));
    }

    [Fact]
    public async Task GetAllTemplates_returns_every_template()
    {
        await Seed(TestData.ApplicationTemplate("First"), TestData.ApplicationTemplate("Second"));

        var templates = await SendAsync(new GetAllTemplates.Query());

        Assert.Equal(["First", "Second"], templates.Select(x => x.Name).Order());
    }

    [Fact]
    public async Task GetAllTemplates_is_allowed_for_a_view_administrator()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team, TestData.ApplicationTemplate());

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        Assert.Single(await SendAsync(caller, new GetAllTemplates.Query()));
    }

    [Fact]
    public async Task GetAllTemplates_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetAllTemplates.Query()));
    }

    // ---- Export ---------------------------------------------------------------------------------

    [Fact]
    public async Task ExportApplicationTemplates_packs_the_requested_templates_only()
    {
        var wanted = TestData.ApplicationTemplate("Wanted");
        await Seed(wanted, TestData.ApplicationTemplate("Unwanted"));

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [wanted.Id] });

        Assert.Equal("Wanted", Assert.Single(ReadTemplates(archive)).Name);
        Assert.False(archive.HasErrors);
    }

    /// <summary>
    /// No ids means everything, which is how a whole installation's templates are exported at once.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_packs_every_template_when_no_ids_are_given()
    {
        await Seed(TestData.ApplicationTemplate("First"), TestData.ApplicationTemplate("Second"));

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });

        Assert.Equal(2, ReadTemplates(archive).Length);
    }

    [Fact]
    public async Task ExportApplicationTemplates_names_the_archive_for_the_requested_type()
    {
        await Seed(TestData.ApplicationTemplate());

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            ArchiveType = ArchiveType.tgz
        });

        Assert.Equal($"{ApplicationConstants.ExportFileName}.tar.gz", archive.Name);
        Assert.Single(ReadTemplates(archive));
    }

    /// <summary>
    /// Without <c>IncludeIcons</c> nothing is fetched: the json keeps the icon urls as they are, and the
    /// archive holds only the json.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_does_not_fetch_icons_by_default()
    {
        var template = TestData.ApplicationTemplate(icon: "https://example.test/icon.png");
        await Seed(template);
        var http = StubHttp();

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });

        Assert.Empty(http.Requests);
        Assert.Equal("https://example.test/icon.png", Assert.Single(ReadTemplates(archive)).Icon);
        Assert.Equal([ApplicationConstants.ExportFileName], Files(archive).Keys);
    }

    /// <summary>
    /// The fetched bytes travel beside the json, so an import can restore icons that were only reachable
    /// from the exporting installation.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_packs_fetched_icons_beside_the_json()
    {
        var template = TestData.ApplicationTemplate(icon: "https://example.test/icon.png");
        await Seed(template);
        var http = StubHttp().Respond("https://example.test/icon.png", IconBytes);

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            IncludeIcons = true
        });

        Assert.Equal(["https://example.test/icon.png"], http.Requests);
        Assert.False(archive.HasErrors);

        var icon = Assert.Single(Files(archive), x => x.Key != ApplicationConstants.ExportFileName);
        Assert.Equal(IconBytes, icon.Value);

        // The url is left alone — the packed bytes are a companion, not a replacement.
        Assert.Equal("https://example.test/icon.png", Assert.Single(ReadTemplates(archive)).Icon);
    }

    /// <summary>
    /// Embedding inlines each icon as a data uri instead, leaving a single self-contained json file.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_inlines_icons_when_embedding()
    {
        var template = TestData.ApplicationTemplate(icon: "https://example.test/icon.png");
        await Seed(template);
        StubHttp().Respond("https://example.test/icon.png", IconBytes);

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            IncludeIcons = true,
            EmbedIcons = true
        });

        Assert.Equal([ApplicationConstants.ExportFileName], Files(archive).Keys);
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(IconBytes)}",
            Assert.Single(ReadTemplates(archive)).Icon);
    }

    /// <summary>
    /// A response with no content type falls back to the media type the icon's extension implies.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_infers_the_media_type_from_the_icon_extension()
    {
        var template = TestData.ApplicationTemplate(icon: "https://example.test/icon.svg");
        await Seed(template);
        StubHttp().Respond("https://example.test/icon.svg", IconBytes, contentType: null);

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            IncludeIcons = true,
            EmbedIcons = true
        });

        Assert.StartsWith("data:image/svg+xml;base64,", Assert.Single(ReadTemplates(archive)).Icon);
    }

    /// <summary>
    /// A failed fetch does not fail the export: the templates still come back, the reason is packed as a
    /// text file, and the flagged result is what tells the caller to look.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_records_a_failed_icon_fetch_as_an_error()
    {
        var template = TestData.ApplicationTemplate("Broken", icon: "https://example.test/missing.png");
        await Seed(template);
        StubHttp().RespondWithStatus("https://example.test/missing.png", HttpStatusCode.NotFound);

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            IncludeIcons = true
        });

        Assert.True(archive.HasErrors);
        Assert.Single(ReadTemplates(archive));

        var errors = Encoding.UTF8.GetString(Files(archive)[ApplicationConstants.ErrorFileName]);
        Assert.Contains("Name: Broken", errors);
        Assert.Contains("Icon: https://example.test/missing.png", errors);
    }

    [Fact]
    public async Task ExportApplicationTemplates_records_a_thrown_icon_fetch_as_an_error()
    {
        var template = TestData.ApplicationTemplate("Unreachable", icon: "https://example.test/boom.png");
        await Seed(template);
        StubHttp().RespondByThrowing("https://example.test/boom.png", new HttpRequestException("no route"));

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            IncludeIcons = true
        });

        Assert.True(archive.HasErrors);
        Assert.Contains(
            "Error: no route",
            Encoding.UTF8.GetString(Files(archive)[ApplicationConstants.ErrorFileName]));
    }

    /// <summary>
    /// An icon that is not an absolute http url is skipped rather than reported — a template with no
    /// icon, or a relative one, is not an error.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_skips_an_icon_that_is_not_a_url()
    {
        await Seed(
            TestData.ApplicationTemplate("No icon", icon: null),
            TestData.ApplicationTemplate("Relative icon", icon: "/assets/icon.png"));

        var http = StubHttp();

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            IncludeIcons = true
        });

        Assert.Empty(http.Requests);
        Assert.False(archive.HasErrors);
        Assert.Equal([ApplicationConstants.ExportFileName], Files(archive).Keys);
    }

    /// <summary>
    /// Characterizes a live 500: this export serializes with <c>UnsafeRelaxedJsonEscaping</c>
    /// (<c>ExportApplicationTemplates.cs:146</c>) so non-ASCII reaches the archive raw, and
    /// <c>ArchiveService.cs:71</c> declares the tar entry's size as a char count. One accented character in
    /// a template name is enough to make every tgz export of it fail. See issue 42.
    /// </summary>
    [Fact]
    public async Task ExportApplicationTemplates_as_tgz_fails_when_a_template_name_is_not_ascii()
    {
        await Seed(TestData.ApplicationTemplate("Übung"));

        await Assert.ThrowsAsync<TarException>(() => SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            ArchiveType = ArchiveType.tgz
        }));
    }

    /// <summary>Zip never declares the size, so the same template exports and reads back intact.</summary>
    [Fact]
    public async Task ExportApplicationTemplates_as_zip_keeps_a_name_that_is_not_ascii()
    {
        await Seed(TestData.ApplicationTemplate("Übung"));

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });

        Assert.Equal("Übung", Assert.Single(ReadTemplates(archive)).Name);
    }

    [Fact]
    public async Task ExportApplicationTemplates_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new ExportApplicationTemplates.Query { Ids = [] }));
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

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });

        // Stand in for the receiving installation, which does not have it yet.
        await SendAsync(new DeleteApplicationTemplate.Command { Id = template.Id });

        var result = await SendAsync(new ImportApplicationTemplates.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive)
        });

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

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });
        await SendAsync(new EditApplicationTemplate.Command { Id = template.Id, Name = "Changed" });

        var result = await SendAsync(new ImportApplicationTemplates.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive)
        });

        Assert.Equal([$"Existing ({template.Id})"], result.Failures);

        await using var db = NewContext();
        Assert.Equal("Changed", (await db.ApplicationTemplates.SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task ImportApplicationTemplates_overwrites_an_existing_template_when_asked()
    {
        var template = TestData.ApplicationTemplate("Original");
        await Seed(template);

        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });
        await SendAsync(new EditApplicationTemplate.Command { Id = template.Id, Name = "Changed" });

        var result = await SendAsync(new ImportApplicationTemplates.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive),
            OverWriteExisting = true
        });

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

        var archive = await SendAsync(new ExportApplicationTemplates.Query
        {
            Ids = [],
            ArchiveType = ArchiveType.tgz
        });

        await SendAsync(new DeleteApplicationTemplate.Command { Id = template.Id });

        var result = await SendAsync(new ImportApplicationTemplates.Command
        {
            Archive = ArchiveHelper.AsFormFile(archive)
        });

        Assert.Empty(result.Failures);

        await using var db = NewContext();
        Assert.Equal("Tarred", (await db.ApplicationTemplates.SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task ImportApplicationTemplates_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Seed(TestData.ApplicationTemplate());
        var archive = await SendAsync(new ExportApplicationTemplates.Query { Ids = [] });

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new ImportApplicationTemplates.Command { Archive = ArchiveHelper.AsFormFile(archive) }));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Points the export handler's client at a stub. Registered on the root host, which is the one
    /// <see cref="ApiTestBase.SendAsync{TResponse}(MediatR.IRequest{TResponse})"/> uses.
    /// </summary>
    private StubHttpMessageHandler StubHttp()
    {
        var handler = new StubHttpMessageHandler();
        RootHost.Resolve<IHttpClientFactory>()
            .CreateClient(Arg.Any<string>())
            .Returns(new HttpClient(handler));

        return handler;
    }

    private Dictionary<string, byte[]> Files(ArchiveResult archive) =>
        ArchiveHelper.ExtractFiles(archive, RootHost.Resolve<IArchiveService>());

    private ApplicationTemplate[] ReadTemplates(ArchiveResult archive) =>
        JsonSerializer.Deserialize<ApplicationTemplate[]>(
            Files(archive)[ApplicationConstants.ExportFileName]);
}
