// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Roles;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Roles;

/// <summary>
/// Covers the <c>Roles</c> feature over HTTP: the real routes, the real middleware, the real claims
/// transformer, the real handlers, the real AutoMapper profiles, a real database.
/// </summary>
/// <remarks>
/// Role names are uniquely indexed and every actor asking for specific system permissions is given a role
/// of its own, so no test here counts the rows in <c>Roles</c> or reads one without naming it. The seeded
/// rows are <c>Administrator</c> (immutable, all permissions) and <c>Content Developer</c> (neither).
/// </remarks>
public class RoleRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_role()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/roles", new { name = "Auditor", allPermissions = true }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<Role>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Auditor", created.Name);
        Assert.True(created.AllPermissions);

        // CreatedAtRoute resolves "getRole", so the Location header points at a route a client can
        // follow. The host is the test server's, so only the path is the endpoint's own doing.
        Assert.Equal($"/api/roles/{created.Id}", response.Headers.Location?.AbsolutePath);

        await using var db = NewContext();
        Assert.True(await db.Roles.AnyAsync(x => x.Id == created.Id, Ct));
    }

    /// <summary>
    /// Names are the identity users see, so a duplicate is a conflict rather than a second row.
    /// </summary>
    [Fact]
    public async Task Create_rejects_a_name_that_is_already_taken()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Conflict,
            await RootClient.PostAsJsonAsync("api/roles", new { name = "Administrator" }, Ct));

        Assert.Equal("A role with that name already exists.", problem.Title);
    }

    /// <summary>
    /// The handler's own duplicate check is the only thing standing between a client and the unique
    /// index, and it runs before the insert — so nothing is persisted by the refused request.
    /// </summary>
    [Fact]
    public async Task Create_does_not_persist_a_role_whose_name_is_already_taken()
    {
        await AssertStatus(
            HttpStatusCode.Conflict,
            await RootClient.PostAsJsonAsync(
                "api/roles", new { name = "Content Developer", allPermissions = true }, Ct));

        await using var db = NewContext();
        var developer = await db.Roles.SingleAsync(x => x.Name == "Content Developer", Ct);
        Assert.Equal(TestData.Roles.ContentDeveloper, developer.Id);
        Assert.False(developer.AllPermissions);
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            "api/roles", new { name = "Nope" }, Ct));
    }

    [Fact]
    public async Task Create_without_an_identity_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().PostAsJsonAsync(
            "api/roles", new { name = "Nobody" }, Ct));
    }

    // ---- Get ------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_role()
    {
        var got = await ReadAsync<Role>(
            await RootClient.GetAsync($"api/roles/{TestData.Roles.Administrator}", Ct));

        Assert.Equal("Administrator", got.Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_role_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/roles/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_without_ViewRoles()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/roles/{TestData.Roles.Administrator}", Ct));
    }

    // ---- GetByName ------------------------------------------------------------------------------

    [Fact]
    public async Task GetByName_returns_the_role()
    {
        var got = await ReadAsync<Role>(await RootClient.GetAsync(ByName("Administrator"), Ct));

        Assert.Equal(TestData.Roles.Administrator, got.Id);
    }

    /// <summary>
    /// The seeded name with a space in it, which only reaches the handler if the route segment is
    /// decoded — an encoded lookup that fell through would be reported as an unknown name.
    /// </summary>
    [Fact]
    public async Task GetByName_returns_a_role_whose_name_contains_a_space()
    {
        var got = await ReadAsync<Role>(await RootClient.GetAsync(ByName("Content Developer"), Ct));

        Assert.Equal(TestData.Roles.ContentDeveloper, got.Id);
    }

    [Fact]
    public async Task GetByName_reports_an_unknown_name_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync(ByName("No Such Role"), Ct));
    }

    // ---- GetAll ---------------------------------------------------------------------------------

    /// <summary>
    /// The seeded roles are enough: this asserts the handler does not filter, not how many rows exist.
    /// </summary>
    [Fact]
    public async Task GetAll_returns_every_role()
    {
        var roles = await ReadAsync<Role[]>(await RootClient.GetAsync("api/roles", Ct));

        Assert.Contains(roles, x => x.Id == TestData.Roles.Administrator);
        Assert.Contains(roles, x => x.Id == TestData.Roles.ContentDeveloper);
    }

    /// <summary>
    /// Either permission admits the caller: roles are needed to render a user list, not only to manage
    /// roles.
    /// </summary>
    [Fact]
    public async Task GetAll_is_allowed_with_only_ViewUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        Assert.NotEmpty(await ReadAsync<Role[]>(await Client(actor).GetAsync("api/roles", Ct)));
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_either_permission()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/roles", Ct));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_renames_the_role()
    {
        var edited = await ReadAsync<Role>(await RootClient.PutAsJsonAsync(
            $"api/roles/{TestData.Roles.ContentDeveloper}", new { name = "Renamed Developer" }, Ct));

        Assert.Equal("Renamed Developer", edited.Name);

        await using var db = NewContext();
        Assert.Equal(
            "Renamed Developer",
            (await db.Roles.SingleAsync(x => x.Id == TestData.Roles.ContentDeveloper, Ct)).Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_role_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PutAsJsonAsync(
            $"api/roles/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    /// <summary>
    /// The same duplicate name <c>Create</c> answers with a 409 is a 500 here, because <c>Edit</c> has no
    /// duplicate check and the unique index refuses the save. Wrong: the caller's mistake is reported as
    /// a server fault.
    /// </summary>
    /// <remarks>
    /// Turns red when <c>Edit</c> gains <c>Create</c>'s check, or catches the update failure — either
    /// makes this a 409.
    /// </remarks>
    [Fact]
    public async Task Edit_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PutAsJsonAsync(
                $"api/roles/{TestData.Roles.ContentDeveloper}", new { name = "Administrator" }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.StartsWith("An error occurred while saving the entity changes", problem.Detail);

        await using var db = NewContext();
        Assert.Equal(
            "Content Developer",
            (await db.Roles.SingleAsync(x => x.Id == TestData.Roles.ContentDeveloper, Ct)).Name);
    }

    /// <summary>
    /// <c>Immutable</c> is on the DTO and seeded true for <c>Administrator</c>, but <c>Edit</c> never
    /// reads it, so the role the system ships with can be renamed. Wrong: the sibling
    /// <c>Permissions</c> and <c>TeamPermissions</c> handlers refuse the same request with a 403.
    /// </summary>
    /// <remarks>
    /// Turns red when <c>Edit</c> checks <c>Immutable</c> as <c>Permissions/Requests/Edit.cs:66</c> does.
    /// </remarks>
    [Fact]
    public async Task Edit_renames_an_immutable_role()
    {
        var edited = await ReadAsync<Role>(await RootClient.PutAsJsonAsync(
            $"api/roles/{TestData.Roles.Administrator}", new { name = "Renamed Administrator" }, Ct));

        Assert.Equal("Renamed Administrator", edited.Name);
        Assert.True(edited.Immutable);

        await using var db = NewContext();
        Assert.Equal(
            "Renamed Administrator",
            (await db.Roles.SingleAsync(x => x.Id == TestData.Roles.Administrator, Ct)).Name);
    }

    /// <summary>
    /// A <c>PUT</c> is a whole-role replacement: the command carries <c>AllPermissions</c> as a
    /// non-nullable bool, so a body that omits it clears the flag rather than leaving it alone.
    /// </summary>
    [Fact]
    public async Task Edit_clears_AllPermissions_when_the_body_omits_it()
    {
        var edited = await ReadAsync<Role>(await RootClient.PutAsJsonAsync(
            $"api/roles/{TestData.Roles.Administrator}", new { name = "Demoted" }, Ct));

        Assert.False(edited.AllPermissions);
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_role()
    {
        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/roles/{TestData.Roles.ContentDeveloper}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Roles.AnyAsync(x => x.Id == TestData.Roles.ContentDeveloper, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_role_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.DeleteAsync($"api/roles/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/roles/{TestData.Roles.ContentDeveloper}", Ct));
    }

    /// <summary>
    /// <c>Delete</c> does not read <c>Immutable</c> either, so a caller holding <c>ManageRoles</c> can
    /// remove the seeded <c>Administrator</c> role and with it every account's administrator grant.
    /// Wrong: <c>Permissions/Requests/Delete.cs:62</c> refuses the same request with a 403.
    /// </summary>
    /// <remarks>
    /// The role is deleted out from under the caller's own user row, which is what makes this worth
    /// pinning. Turns red when <c>Delete</c> checks <c>Immutable</c>.
    /// </remarks>
    [Fact]
    public async Task Delete_removes_an_immutable_role()
    {
        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/roles/{TestData.Roles.Administrator}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Roles.AnyAsync(x => x.Id == TestData.Roles.Administrator, Ct));
        Assert.Null((await db.Users.SingleAsync(x => x.Id == Root.Id, Ct)).RoleId);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// The <c>GetByName</c> route for <paramref name="name"/>. Escaped, because the seeded names and the
    /// names a test invents both contain spaces.
    /// </summary>
    private static string ByName(string name) => $"api/roles/name/{Uri.EscapeDataString(name)}";
}
