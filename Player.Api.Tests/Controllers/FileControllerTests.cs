// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Controllers;

/// <summary>
/// Covers <c>FileController</c> over HTTP. Everything here needs the request: multipart form binding,
/// the result types the controller returns, and the download response's headers, none of which
/// <c>FileServiceTests</c> can see from behind the service.
/// </summary>
public class FileControllerTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Upload ---------------------------------------------------------------------------------

    /// <summary>
    /// One model per uploaded file, in the order the parts appear in the form — which the endpoint's
    /// own remarks promise and only a multi-part request can check.
    /// </summary>
    [Fact]
    public async Task Upload_returns_a_model_per_file_in_form_order()
    {
        var (view, team) = await ViewWithTeam();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(view.Id.ToString()), "viewId" },
            { new StringContent(team.Id.ToString()), "teamIds" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("first")), "ToUpload", "first.txt" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("second")), "ToUpload", "second.txt" }
        };

        var response = await RootClient.PostAsync("api/files", form, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var uploaded = await ReadAsync<FileModel[]>(response);

        Assert.Equal(2, uploaded.Length);
        Assert.Equal("first.txt", uploaded[0].Name);
        Assert.Equal("second.txt", uploaded[1].Name);
        Assert.Equal(team.Id, Assert.Single(uploaded[0].teamIds));

        await using var db = NewContext();
        Assert.Equal(2, await db.Files.CountAsync(x => x.View.Id == view.Id, Ct));
    }

    /// <summary>
    /// The models the upload returns carry an all-zeros <c>viewId</c>. <c>FileEntity</c> holds its view
    /// only as a navigation, and <c>UploadAsync</c> maps the entity to its model
    /// (<c>FileService.cs:91</c>) before adding it to <c>viewEntity.Files</c> — so the navigation is
    /// still null when the response body is built. Reading the same file back fills it in.
    /// </summary>
    [Fact]
    public async Task Upload_answers_with_models_that_do_not_name_the_view()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "unnamed.txt");

        Assert.Equal(Guid.Empty, uploaded.viewId);

        var reread = await ReadAsync<FileModel>(await RootClient.GetAsync($"api/files/{uploaded.id}", Ct));

        Assert.Equal(view.Id, reread.viewId);
    }

    /// <summary>
    /// <c>CreatedAtAction(nameof(Get))</c> names the collection action, so the 201's <c>Location</c>
    /// points at every file in the system rather than at anything just created.
    /// </summary>
    [Fact]
    public async Task Upload_points_the_location_header_at_the_whole_collection()
    {
        var (view, team) = await ViewWithTeam();

        using var form = Upload(view.Id, team.Id, "located.txt");
        var response = await RootClient.PostAsync("api/files", form, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.Equal("/api/files", response.Headers.Location?.AbsolutePath);
    }

    /// <summary>
    /// The allow-list is <c>FileUpload:allowedExtensions</c> in <c>appsettings.json</c>, and a
    /// rejection is a 403 rather than a 400.
    /// </summary>
    [Fact]
    public async Task Upload_rejects_an_extension_that_is_not_allowed()
    {
        var (view, team) = await ViewWithTeam();

        using var form = Upload(view.Id, team.Id, "payload.exe");

        await AssertStatus(HttpStatusCode.Forbidden, await RootClient.PostAsync("api/files", form, Ct));
    }

    /// <summary>
    /// A form with no <c>teamIds</c> part binds the list as null, which the service reads as "assigned
    /// to no team" and refuses.
    /// </summary>
    [Fact]
    public async Task Upload_requires_at_least_one_team()
    {
        var (view, _) = await ViewWithTeam();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(view.Id.ToString()), "viewId" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("orphan")), "ToUpload", "orphan.txt" }
        };

        var response = await RootClient.PostAsync("api/files", form, Ct);

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Contains("at least one team", await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Uploading needs <c>ManageViews</c> or <c>ManageView</c> on the view.</summary>
    [Fact]
    public async Task Upload_is_forbidden_for_a_caller_who_cannot_manage_the_view()
    {
        var (view, team) = await ViewWithTeam();
        var actor = await Actor().SeedAsync();

        using var form = Upload(view.Id, team.Id, "denied.txt");

        await AssertStatus(
            HttpStatusCode.Forbidden,
            await Client(actor).PostAsync("api/files", form, Ct));
    }

    // ---- Read -----------------------------------------------------------------------------------

    /// <summary>The collection route is system-wide: files from every view, for <c>ViewViews</c>.</summary>
    [Fact]
    public async Task GetAll_returns_the_files_of_every_view()
    {
        var (first, firstTeam) = await ViewWithTeam();
        var (second, secondTeam) = await ViewWithTeam();

        await Uploaded(RootClient, first.Id, firstTeam.Id, "one.txt");
        await Uploaded(RootClient, second.Id, secondTeam.Id, "two.txt");

        var files = await ReadAsync<FileModel[]>(await RootClient.GetAsync("api/files", Ct));

        Assert.Equal(2, files.Length);
        Assert.Contains(files, x => x.viewId == first.Id);
        Assert.Contains(files, x => x.viewId == second.Id);
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_view_views()
    {
        var actor = await Actor().SeedAsync();

        await AssertStatus(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/files", Ct));
    }

    /// <summary>
    /// The default path serves the teams the caller can see, which for a member holding only
    /// <c>ViewTeam</c> on its primary team is that team alone.
    /// </summary>
    [Fact]
    public async Task GetViewFiles_returns_only_the_files_of_the_callers_teams_by_default()
    {
        var (view, mine, theirs) = await ViewWithTwoTeams();
        var actor = await Actor().OnTeam(mine, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await Uploaded(RootClient, view.Id, mine.Id, "mine.txt");
        await Uploaded(RootClient, view.Id, theirs.Id, "theirs.txt");

        var files = await ReadAsync<FileModel[]>(
            await Client(actor).GetAsync($"api/views/{view.Id}/files", Ct));

        Assert.Equal("mine.txt", Assert.Single(files).Name);
    }

    /// <summary>
    /// <c>includeAllViewFiles</c> is not what decides how much a caller sees. <c>ViewView</c> on the
    /// primary team widens the visibility context to every team in the view
    /// (<c>AuthorizationService.cs:173-184</c>), so the default path already answers with the whole
    /// view's files.
    /// </summary>
    [Fact]
    public async Task GetViewFiles_returns_every_file_by_default_for_a_caller_who_can_view_the_view()
    {
        var (view, mine, theirs) = await ViewWithTwoTeams();
        var actor = await Actor().OnTeam(mine, viewPermissions: [ViewPermission.ViewView]).SeedAsync();

        await Uploaded(RootClient, view.Id, mine.Id, "mine.txt");
        await Uploaded(RootClient, view.Id, theirs.Id, "theirs.txt");

        var files = await ReadAsync<FileModel[]>(
            await Client(actor).GetAsync($"api/views/{view.Id}/files", Ct));

        Assert.Equal(2, files.Length);
    }

    [Fact]
    public async Task GetViewFiles_returns_every_file_in_the_view_when_asked()
    {
        var (view, mine, theirs) = await ViewWithTwoTeams();

        await Uploaded(RootClient, view.Id, mine.Id, "mine.txt");
        await Uploaded(RootClient, view.Id, theirs.Id, "theirs.txt");

        var files = await ReadAsync<FileModel[]>(await RootClient
            .GetAsync($"api/views/{view.Id}/files?includeAllViewFiles=true", Ct));

        Assert.Equal(2, files.Length);
    }

    /// <summary>
    /// Asking for the whole view is authorized against the view, so the team-scoped caller the default
    /// path serves is refused here rather than being narrowed to what it may see.
    /// </summary>
    [Fact]
    public async Task GetViewFiles_forbids_the_whole_view_to_a_caller_scoped_to_one_team()
    {
        var (view, mine, _) = await ViewWithTwoTeams();
        var actor = await Actor().OnTeam(mine, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        await AssertStatus(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/views/{view.Id}/files?includeAllViewFiles=true", Ct));
    }

    /// <summary>
    /// Both paths check the view, from different places: the file service's own existence check on the
    /// <c>includeAllViewFiles</c> path, and the team service's on the default one.
    /// </summary>
    [Fact]
    public async Task GetViewFiles_is_not_found_for_an_unknown_view_on_either_path()
    {
        var unknown = Guid.NewGuid();

        await AssertStatus(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/views/{unknown}/files?includeAllViewFiles=true", Ct));

        await AssertStatus(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/views/{unknown}/files", Ct));
    }

    [Fact]
    public async Task GetTeamFiles_returns_the_files_assigned_to_the_team()
    {
        var (view, mine, theirs) = await ViewWithTwoTeams();

        await Uploaded(RootClient, view.Id, mine.Id, "mine.txt");
        await Uploaded(RootClient, view.Id, theirs.Id, "theirs.txt");

        var files = await ReadAsync<FileModel[]>(
            await RootClient.GetAsync($"api/teams/{mine.Id}/files", Ct));

        Assert.Equal("mine.txt", Assert.Single(files).Name);
    }

    [Fact]
    public async Task GetById_returns_the_file()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "wanted.txt");

        var file = await ReadAsync<FileModel>(await RootClient.GetAsync($"api/files/{uploaded.id}", Ct));

        Assert.Equal(uploaded.id, file.id);
        Assert.Equal("wanted.txt", file.Name);
    }

    [Fact]
    public async Task GetById_is_not_found_for_an_unknown_id()
    {
        await AssertStatus(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/files/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// A route value that is not a Guid never reaches the action: <c>[ApiController]</c> answers the
    /// binding failure itself, and that one is a <c>problem+json</c> 400.
    /// </summary>
    [Fact]
    public async Task GetById_is_a_bad_request_for_an_id_that_is_not_a_guid()
    {
        await AssertProblem(
            HttpStatusCode.BadRequest,
            await RootClient.GetAsync("api/files/not-a-guid", Ct));
    }

    /// <summary>
    /// Controllers answer a handled exception through <c>JsonExceptionFilter</c>, not
    /// <c>ExceptionMiddleware</c>, so the body is <c>application/json</c> where every minimal-API route
    /// returns <c>application/problem+json</c>. Same status, two shapes, one API.
    /// </summary>
    [Fact]
    public async Task A_handled_exception_is_answered_as_json_rather_than_problem_json()
    {
        var response = await RootClient.GetAsync($"api/files/{Guid.NewGuid()}", Ct);

        await AssertStatus(HttpStatusCode.NotFound, response);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("api/files", Ct));
    }

    // ---- Download -------------------------------------------------------------------------------

    /// <summary>
    /// The stored bytes, served as a download with the name the file was uploaded under rather than the
    /// Guid it is stored as.
    /// </summary>
    [Fact]
    public async Task Download_streams_the_stored_bytes_as_an_attachment()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "notes.txt", "the stored bytes");

        var response = await RootClient.GetAsync($"api/files/download/{uploaded.id}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("the stored bytes", await response.Content.ReadAsStringAsync(Ct));
        Assert.Contains("notes.txt", response.Content.Headers.ContentDisposition?.ToString());
    }

    /// <summary>
    /// A PDF is served as <c>application/pdf</c>. The <c>Content-Disposition: inline</c> the controller
    /// appends first does not survive: naming a download file makes the file result assign the header,
    /// replacing the value rather than adding to it, so the browser is still told to download.
    /// </summary>
    [Fact]
    public async Task Download_serves_a_pdf_as_a_pdf_but_not_inline()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "guide.pdf");

        var response = await RootClient.GetAsync($"api/files/download/{uploaded.id}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.StartsWith("attachment", disposition);
        Assert.DoesNotContain("inline", disposition);
    }

    /// <summary>
    /// An image's content type is built from its extension, so the subtype is whatever followed the
    /// last dot in the uploaded name.
    /// </summary>
    [Fact]
    public async Task Download_serves_an_image_with_its_extension_as_the_content_subtype()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "diagram.png");

        var response = await RootClient.GetAsync($"api/files/download/{uploaded.id}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Download_is_not_found_for_an_unknown_id()
    {
        await AssertStatus(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/files/download/{Guid.NewGuid()}", Ct));
    }

    /// <summary>A caller with no access to any of the file's teams cannot download it.</summary>
    [Fact]
    public async Task Download_is_forbidden_for_a_caller_with_no_access_to_the_file()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "private.txt");
        var actor = await Actor().SeedAsync();

        await AssertStatus(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/files/download/{uploaded.id}", Ct));
    }

    // ---- Update ---------------------------------------------------------------------------------

    /// <summary>A form with no file renames the entry and reassigns its teams.</summary>
    [Fact]
    public async Task Update_renames_the_file_and_reassigns_its_teams()
    {
        var (view, first, second) = await ViewWithTwoTeams();
        var uploaded = await Uploaded(RootClient, view.Id, first.Id, "before.txt");

        using var form = new MultipartFormDataContent
        {
            { new StringContent("after.txt"), "Name" },
            { new StringContent(second.Id.ToString()), "TeamIds" }
        };

        var updated = await ReadAsync<FileModel>(
            await RootClient.PutAsync($"api/files/{uploaded.id}", form, Ct));

        Assert.Equal("after.txt", updated.Name);
        Assert.Equal(second.Id, Assert.Single(updated.teamIds));

        await using var db = NewContext();
        Assert.Equal("after.txt", (await db.Files.SingleAsync(x => x.Id == uploaded.id, Ct)).Name);
    }

    /// <summary>
    /// A form carrying a file replaces the bytes and takes the new file's name. The teams in the same
    /// form are validated against the view and then not applied — the two branches are exclusive.
    /// </summary>
    [Fact]
    public async Task Update_replaces_the_stored_bytes_and_leaves_the_teams_alone()
    {
        var (view, first, second) = await ViewWithTwoTeams();
        var uploaded = await Uploaded(RootClient, view.Id, first.Id, "original.txt", "original bytes");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(second.Id.ToString()), "TeamIds" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("new bytes")), "ToUpload", "replacement.txt" }
        };

        var updated = await ReadAsync<FileModel>(
            await RootClient.PutAsync($"api/files/{uploaded.id}", form, Ct));

        Assert.Equal("replacement.txt", updated.Name);
        Assert.Equal(first.Id, Assert.Single(updated.teamIds));

        var download = await RootClient.GetAsync($"api/files/download/{uploaded.id}", Ct);
        Assert.Equal("new bytes", await download.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// A form with no <c>TeamIds</c> part is a 500: the upload path guards a null list and refuses, while
    /// <c>UpdateAsync</c> hands it straight to <c>TeamsInSameView</c>, whose <c>foreach</c> dereferences it
    /// (<c>FileService.cs:214,382</c>). So a rename cannot be sent on its own, though its 404 sibling
    /// above can, because the entity lookup comes first.
    /// </summary>
    /// <remarks>Turns red when the update path guards the list as the upload path does.</remarks>
    [Fact]
    public async Task Update_without_teams_is_a_server_error()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "keeping.txt");

        using var form = new MultipartFormDataContent
        {
            { new StringContent("renamed.txt"), "Name" }
        };

        await AssertStatus(
            HttpStatusCode.InternalServerError,
            await RootClient.PutAsync($"api/files/{uploaded.id}", form, Ct));
    }

    [Fact]
    public async Task Update_is_not_found_for_an_unknown_id()
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("renamed.txt"), "Name" }
        };

        await AssertStatus(
            HttpStatusCode.NotFound,
            await RootClient.PutAsync($"api/files/{Guid.NewGuid()}", form, Ct));
    }

    // ---- Delete ---------------------------------------------------------------------------------

    /// <summary>
    /// 204 and no body, and the bytes go with the row because nothing else points at them.
    /// </summary>
    [Fact]
    public async Task Delete_removes_the_row_and_the_stored_bytes()
    {
        var (view, team) = await ViewWithTeam();
        var uploaded = await Uploaded(RootClient, view.Id, team.Id, "gone.txt");

        string path;

        await using (var before = NewContext())
        {
            path = (await before.Files.SingleAsync(x => x.Id == uploaded.id, Ct)).Path;
        }

        Assert.True(File.Exists(path));

        var response = await RootClient.DeleteAsync($"api/files/{uploaded.id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.False(File.Exists(path));

        await using var after = NewContext();
        Assert.False(await after.Files.AnyAsync(x => x.Id == uploaded.id, Ct));
    }

    [Fact]
    public async Task Delete_is_not_found_for_an_unknown_id()
    {
        await AssertStatus(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/files/{Guid.NewGuid()}", Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private async Task<(ViewEntity View, TeamEntity Team)> ViewWithTeam()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        return (view, team);
    }

    private async Task<(ViewEntity View, TeamEntity First, TeamEntity Second)> ViewWithTwoTeams()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        await Seed(view, first, second);

        return (view, first, second);
    }

    /// <summary>An upload form for one file. The caller owns it.</summary>
    private static MultipartFormDataContent Upload(
        Guid viewId,
        Guid teamId,
        string fileName,
        string content = "contents")
    {
        return new MultipartFormDataContent
        {
            { new StringContent(viewId.ToString()), "viewId" },
            { new StringContent(teamId.ToString()), "teamIds" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "ToUpload", fileName }
        };
    }

    /// <summary>Uploads one file and returns its model, for the tests that need a file to act on.</summary>
    private async Task<FileModel> Uploaded(
        HttpClient client,
        Guid viewId,
        Guid teamId,
        string fileName,
        string content = "contents")
    {
        using var form = Upload(viewId, teamId, fileName, content);
        var uploaded = await ReadAsync<FileModel[]>(await client.PostAsync("api/files", form, Ct));

        return uploaded.Single();
    }
}
