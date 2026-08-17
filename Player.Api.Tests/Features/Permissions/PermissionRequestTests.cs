// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Permissions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Permissions;

/// <summary>
/// Covers the <c>Permissions</c> feature over HTTP: the system permission rows themselves, their grants
/// to system roles, and the claims-only <c>GetMine</c> query.
/// </summary>
public class PermissionRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_permission()
    {
        var response = await RootClient.PostAsJsonAsync(
            "api/permissions",
            new { name = "Custom", description = "A custom one" },
            Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var created = await ReadAsync<Permission>(response);

        Assert.Equal("Custom", created.Name);

        // The only assertion in this file on the route name CreatedAtRoute resolves, which is what makes
        // the Location header point at something a client can follow.
        Assert.Equal($"/api/permissions/{created.Id}", response.Headers.Location?.AbsolutePath);

        // The stored row rather than the response, since the response is mapped from the entity the
        // handler holds in memory and would read the same whether or not the save took the values with it.
        await using var db = NewContext();
        var stored = await db.Permissions.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Custom", stored.Name);
        Assert.Equal("A custom one", stored.Description);
    }

    /// <summary>
    /// Names are uniquely indexed because authorization resolves a permission by name, but the handler
    /// saves without looking, so a client's duplicate comes back as a server error rather than a 409.
    /// </summary>
    /// <remarks>
    /// Turns red when the handler checks the name first, or throws something that maps to a client
    /// error. The detail is asserted because it is what says the failure reached the save.
    /// </remarks>
    [Fact]
    public async Task Create_reports_a_duplicate_name_as_a_server_error()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync(
                "api/permissions",
                new { name = SystemPermission.CreateViews.ToString(), description = "A second one" },
                Ct));

        Assert.StartsWith("An error occurred while saving the entity changes.", problem.Detail);

        await using var db = NewContext();
        Assert.Equal(
            1,
            await db.Permissions.CountAsync(
                x => x.Name == SystemPermission.CreateViews.ToString(), Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            "api/permissions", new { name = "Nope" }, Ct));
    }

    [Fact]
    public async Task Edit_updates_a_mutable_permission()
    {
        var permissionId = TestData.Permissions.ViewNetworks;

        var edited = await ReadAsync<Permission>(await RootClient.PutAsJsonAsync(
            $"api/permissions/{permissionId}",
            new { name = "ViewNetworks", description = "Reworded" },
            Ct));

        Assert.Equal("Reworded", edited.Description);

        await using var db = NewContext();
        Assert.Equal(
            "Reworded",
            (await db.Permissions.SingleAsync(x => x.Id == permissionId, Ct)).Description);
    }

    /// <summary>
    /// The immutable flag protects the permissions the application's own authorization depends on:
    /// renaming one would silently revoke it from every role that grants it.
    /// </summary>
    /// <remarks>
    /// The title is asserted because the caller holds every system permission, so it is what says the
    /// 403 is the flag's refusal rather than authorization's.
    /// </remarks>
    [Fact]
    public async Task Edit_refuses_an_immutable_permission()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Forbidden,
            await RootClient.PutAsJsonAsync(
                $"api/permissions/{TestData.Permissions.CreateViews}",
                new { name = "Renamed" },
                Ct));

        Assert.Equal("Cannot update an Immutable Permission", problem.Title);
    }

    [Fact]
    public async Task Edit_reports_a_missing_permission_as_not_found()
    {
        await AssertNotFound("Permission", await RootClient.PutAsJsonAsync(
            $"api/permissions/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    [Fact]
    public async Task Delete_removes_a_mutable_permission()
    {
        var permissionId = TestData.Permissions.ViewNetworks;

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/permissions/{permissionId}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Permissions.AnyAsync(x => x.Id == permissionId, Ct));
    }

    [Fact]
    public async Task Delete_refuses_an_immutable_permission()
    {
        var problem = await AssertProblem(
            HttpStatusCode.Forbidden,
            await RootClient.DeleteAsync($"api/permissions/{TestData.Permissions.CreateViews}", Ct));

        Assert.Equal("Cannot delete an Immutable Permission", problem.Title);
    }

    [Fact]
    public async Task Delete_reports_a_missing_permission_as_not_found()
    {
        await AssertNotFound(
            "Permission",
            await RootClient.DeleteAsync($"api/permissions/{Guid.NewGuid()}", Ct));
    }

    // ---- Get / GetAll / GetMine -----------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_permission()
    {
        var got = await ReadAsync<Permission>(await RootClient.GetAsync(
            $"api/permissions/{TestData.Permissions.CreateViews}", Ct));

        Assert.Equal(SystemPermission.CreateViews.ToString(), got.Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_permission_as_not_found()
    {
        await AssertNotFound(
            "Permission",
            await RootClient.GetAsync($"api/permissions/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// Permission rows are not public: a caller holding nothing is refused rather than shown the
    /// vocabulary the system authorizes by.
    /// </summary>
    /// <remarks>
    /// The actor is on no team either, because <c>Authorize</c> also admits <c>ViewView</c> in any
    /// view — a membership carrying it would have answered the request rather than refusing it.
    /// </remarks>
    [Fact]
    public async Task Get_is_forbidden_without_ViewRoles()
    {
        var actor = await Actor().SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync(
            $"api/permissions/{TestData.Permissions.CreateViews}", Ct));
    }

    /// <summary>
    /// Unfiltered by design, and named rather than counted: a migration seeds a row for every
    /// permission the application resolves by name, so an exact count would only restate the seed.
    /// </summary>
    [Fact]
    public async Task GetAll_returns_every_permission()
    {
        var permissions = await ReadAsync<Permission[]>(
            await RootClient.GetAsync("api/permissions", Ct));

        Assert.Contains(permissions, x => x.Id == TestData.Permissions.CreateViews);
        Assert.Contains(permissions, x => x.Id == TestData.Permissions.ViewNetworks);
    }

    /// <summary>
    /// Open to any caller, and answered from the claims rather than the database — it reports what the
    /// caller holds, so there is nothing to authorize.
    /// </summary>
    [Fact]
    public async Task GetMine_returns_the_caller_system_permissions()
    {
        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.CreateViews, SystemPermission.ViewUsers)
            .SeedAsync();

        var permissions = await ReadAsync<string[]>(
            await Client(actor).GetAsync("api/permissions/mine", Ct));

        Assert.Equal(
            [SystemPermission.CreateViews.ToString(), SystemPermission.ViewUsers.ToString()],
            permissions.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetMine_returns_nothing_for_a_caller_holding_no_permissions()
    {
        var actor = await Actor().SeedAsync();

        Assert.Empty(await ReadAsync<string[]>(
            await Client(actor).GetAsync("api/permissions/mine", Ct)));
    }

    // ---- AddToRole / RemoveFromRole -------------------------------------------------------------

    [Fact]
    public async Task AddToRole_grants_the_permission_to_the_role()
    {
        var roleId = TestData.Roles.ContentDeveloper;
        var permissionId = TestData.Permissions.ViewNetworks;

        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync(
            $"api/roles/{roleId}/permissions/{permissionId}", null, Ct));

        await using var db = NewContext();
        Assert.True(await db.RolePermissions.AnyAsync(
            x => x.RoleId == roleId && x.PermissionId == permissionId, Ct));
    }

    /// <summary>
    /// Granting a permission the role already holds is a server error, where revoking one the role does
    /// not hold is a success — the same request repeated succeeds once and then 500s.
    /// </summary>
    /// <remarks>
    /// <c>(RoleId, PermissionId)</c> is uniquely indexed and the handler adds without looking, so the
    /// insert fails in the database. The seeded <c>Content Developer</c> row already grants
    /// <c>CreateViews</c>, which is the existing grant this repeats.
    /// </remarks>
    [Fact]
    public async Task AddToRole_reports_a_grant_the_role_already_holds_as_a_server_error()
    {
        var roleId = TestData.Roles.ContentDeveloper;
        var permissionId = TestData.Permissions.CreateViews;

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.PostAsync($"api/roles/{roleId}/permissions/{permissionId}", null, Ct));

        Assert.StartsWith("An error occurred while saving the entity changes.", problem.Detail);

        await using var db = NewContext();
        Assert.Equal(
            1,
            await db.RolePermissions.CountAsync(
                x => x.RoleId == roleId && x.PermissionId == permissionId, Ct));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_role_as_not_found()
    {
        var permissionId = TestData.Permissions.ViewNetworks;

        await AssertNotFound("Role", await RootClient.PostAsync(
            $"api/roles/{Guid.NewGuid()}/permissions/{permissionId}", null, Ct));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_permission_as_not_found()
    {
        var roleId = TestData.Roles.ContentDeveloper;

        await AssertNotFound("Permission", await RootClient.PostAsync(
            $"api/roles/{roleId}/permissions/{Guid.NewGuid()}", null, Ct));
    }

    [Fact]
    public async Task AddToRole_is_forbidden_without_ManageRoles()
    {
        var roleId = TestData.Roles.ContentDeveloper;
        var permissionId = TestData.Permissions.ViewNetworks;

        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsync(
            $"api/roles/{roleId}/permissions/{permissionId}", null, Ct));
    }

    [Fact]
    public async Task RemoveFromRole_revokes_the_permission()
    {
        var roleId = TestData.Roles.ContentDeveloper;
        var permissionId = TestData.Permissions.ViewNetworks;
        await Seed(new RolePermissionEntity(roleId, permissionId) { Id = Guid.NewGuid() });

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/roles/{roleId}/permissions/{permissionId}", Ct));

        await using var db = NewContext();
        Assert.False(await db.RolePermissions.AnyAsync(
            x => x.RoleId == roleId && x.PermissionId == permissionId, Ct));
    }

    /// <summary>
    /// A grant that is not there is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task RemoveFromRole_does_nothing_when_the_role_does_not_hold_the_permission()
    {
        var roleId = TestData.Roles.ContentDeveloper;
        var permissionId = TestData.Permissions.ViewNetworks;

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync(
            $"api/roles/{roleId}/permissions/{permissionId}", Ct));
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_role_as_not_found()
    {
        var permissionId = TestData.Permissions.ViewNetworks;

        await AssertNotFound("Role", await RootClient.DeleteAsync(
            $"api/roles/{Guid.NewGuid()}/permissions/{permissionId}", Ct));
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_permission_as_not_found()
    {
        var roleId = TestData.Roles.ContentDeveloper;

        await AssertNotFound("Permission", await RootClient.DeleteAsync(
            $"api/roles/{roleId}/permissions/{Guid.NewGuid()}", Ct));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Asserts a 404 naming <paramref name="entity"/>, which is what tells the two not-found cases of
    /// one route apart: <c>EntityNotFoundException&lt;T&gt;</c> builds its message from the type name
    /// and <c>ExceptionMiddleware</c> answers with the message as the title.
    /// </summary>
    private static async Task AssertNotFound(string entity, HttpResponseMessage response) =>
        Assert.Equal(
            $"{entity} not found",
            (await AssertProblem(HttpStatusCode.NotFound, response)).Title);
}
