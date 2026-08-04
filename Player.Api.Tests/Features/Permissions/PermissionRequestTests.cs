// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Permissions;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Permissions;

/// <summary>
/// Covers the <c>Permissions</c> feature's request handlers, including the role membership commands.
/// </summary>
public class PermissionRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_permission()
    {
        var created = await SendAsync(new Create.Command { Name = "Custom", Description = "A custom one" });

        Assert.Equal("Custom", created.Name);

        await using var db = NewContext();
        Assert.True(await db.Permissions.AnyAsync(x => x.Id == created.Id, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Create.Command { Name = "Nope" }));
    }

    [Fact]
    public async Task Edit_updates_a_mutable_permission()
    {
        var edited = await SendAsync(new Edit.Command
        {
            Id = TestData.Permissions.ViewNetworks,
            Name = "ViewNetworks",
            Description = "Reworded"
        });

        Assert.Equal("Reworded", edited.Description);
    }

    /// <summary>
    /// The immutable flag protects the permissions the application's own authorization depends on:
    /// renaming one would silently revoke it from every role that grants it.
    /// </summary>
    [Fact]
    public async Task Edit_refuses_an_immutable_permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(new Edit.Command
        {
            Id = TestData.Permissions.CreateViews,
            Name = "Renamed"
        }));
    }

    [Fact]
    public async Task Edit_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Permission>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task Delete_removes_a_mutable_permission()
    {
        await SendAsync(new Delete.Command { Id = TestData.Permissions.ViewNetworks });

        await using var db = NewContext();
        Assert.False(await db.Permissions.AnyAsync(x => x.Id == TestData.Permissions.ViewNetworks, Ct));
    }

    [Fact]
    public async Task Delete_refuses_an_immutable_permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(new Delete.Command { Id = TestData.Permissions.CreateViews }));
    }

    [Fact]
    public async Task Delete_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Permission>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    // ---- Get / GetAll / GetMine -----------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_permission()
    {
        Assert.Equal(
            SystemPermission.CreateViews.ToString(),
            (await SendAsync(new Get.Query { Id = TestData.Permissions.CreateViews })).Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Permission>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Get_is_forbidden_without_ViewRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Get.Query { Id = TestData.Permissions.CreateViews }));
    }

    [Fact]
    public async Task GetAll_returns_every_permission()
    {
        var permissions = await SendAsync(new GetAll.Query());

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
        var caller = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.CreateViews, SystemPermission.ViewUsers)
            .Build();

        var permissions = await SendAsync(caller, new GetMine.Query());

        Assert.Equal(
            [SystemPermission.CreateViews.ToString(), SystemPermission.ViewUsers.ToString()],
            permissions.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetMine_returns_nothing_for_a_caller_holding_no_permissions()
    {
        Assert.Empty(await SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetMine.Query()));
    }

    // ---- AddToRole / RemoveFromRole -------------------------------------------------------------

    [Fact]
    public async Task AddToRole_grants_the_permission_to_the_role()
    {
        await SendAsync(new AddToRole.Command
        {
            RoleId = TestData.Roles.ContentDeveloper,
            PermissionId = TestData.Permissions.ViewNetworks
        });

        await using var db = NewContext();
        Assert.True(await db.RolePermissions.AnyAsync(
            x => x.RoleId == TestData.Roles.ContentDeveloper &&
                 x.PermissionId == TestData.Permissions.ViewNetworks,
            Ct));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Roles.Role>>(
            () => SendAsync(new AddToRole.Command
            {
                RoleId = Guid.NewGuid(),
                PermissionId = TestData.Permissions.ViewNetworks
            }));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Permission>>(
            () => SendAsync(new AddToRole.Command
            {
                RoleId = TestData.Roles.ContentDeveloper,
                PermissionId = Guid.NewGuid()
            }));
    }

    [Fact]
    public async Task AddToRole_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new AddToRole.Command
            {
                RoleId = TestData.Roles.ContentDeveloper,
                PermissionId = TestData.Permissions.ViewNetworks
            }));
    }

    [Fact]
    public async Task RemoveFromRole_revokes_the_permission()
    {
        await Seed(new RolePermissionEntity(TestData.Roles.ContentDeveloper, TestData.Permissions.ViewNetworks));

        await SendAsync(new RemoveFromRole.Command
        {
            RoleId = TestData.Roles.ContentDeveloper,
            PermissionId = TestData.Permissions.ViewNetworks
        });

        await using var db = NewContext();
        Assert.False(await db.RolePermissions.AnyAsync(
            x => x.RoleId == TestData.Roles.ContentDeveloper &&
                 x.PermissionId == TestData.Permissions.ViewNetworks,
            Ct));
    }

    /// <summary>
    /// A grant that is not there is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task RemoveFromRole_does_nothing_when_the_role_does_not_hold_the_permission()
    {
        await SendAsync(new RemoveFromRole.Command
        {
            RoleId = TestData.Roles.ContentDeveloper,
            PermissionId = TestData.Permissions.ViewNetworks
        });
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Roles.Role>>(
            () => SendAsync(new RemoveFromRole.Command
            {
                RoleId = Guid.NewGuid(),
                PermissionId = TestData.Permissions.ViewNetworks
            }));
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Permission>>(
            () => SendAsync(new RemoveFromRole.Command
            {
                RoleId = TestData.Roles.ContentDeveloper,
                PermissionId = Guid.NewGuid()
            }));
    }
}
