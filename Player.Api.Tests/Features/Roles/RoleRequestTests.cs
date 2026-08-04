// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Roles;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Roles;

/// <summary>
/// Covers the <c>Roles</c> feature's request handlers, over the roles <c>PlayerContext</c> seeds.
/// </summary>
public class RoleRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Create_persists_the_role()
    {
        var created = await SendAsync(new Create.Command { Name = "Auditor", AllPermissions = true });

        Assert.Equal("Auditor", created.Name);
        Assert.True(created.AllPermissions);

        await using var db = NewContext();
        Assert.True(await db.Roles.AnyAsync(x => x.Id == created.Id, Ct));
    }

    /// <summary>
    /// Names are the identity users see, so a duplicate is a conflict rather than a second row.
    /// </summary>
    [Fact]
    public async Task Create_rejects_a_name_that_is_already_taken()
    {
        await Assert.ThrowsAsync<ConflictException>(
            () => SendAsync(new Create.Command { Name = "Administrator" }));
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Create.Command { Name = "Nope" }));
    }

    [Fact]
    public async Task Get_returns_the_role()
    {
        Assert.Equal(
            "Administrator",
            (await SendAsync(new Get.Query { Id = TestData.Roles.Administrator })).Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Role>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Get_is_forbidden_without_ViewRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Get.Query { Id = TestData.Roles.Administrator }));
    }

    [Fact]
    public async Task GetByName_returns_the_role()
    {
        Assert.Equal(
            TestData.Roles.Administrator,
            (await SendAsync(new GetByName.Query { Name = "Administrator" })).Id);
    }

    [Fact]
    public async Task GetByName_reports_an_unknown_name_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Role>>(
            () => SendAsync(new GetByName.Query { Name = "No Such Role" }));
    }

    /// <summary>
    /// The seeded roles are enough: this asserts the handler does not filter, not how many rows exist.
    /// </summary>
    [Fact]
    public async Task GetAll_returns_every_role()
    {
        var roles = await SendAsync(new GetAll.Query());

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
        var caller = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewUsers)
            .Build();

        Assert.NotEmpty(await SendAsync(caller, new GetAll.Query()));
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_either_permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetAll.Query()));
    }

    [Fact]
    public async Task Edit_renames_the_role()
    {
        var edited = await SendAsync(new Edit.Command
        {
            Id = TestData.Roles.ContentDeveloper,
            Name = "Renamed Developer"
        });

        Assert.Equal("Renamed Developer", edited.Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Role>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task Delete_removes_the_role()
    {
        await SendAsync(new Delete.Command { Id = TestData.Roles.ContentDeveloper });

        await using var db = NewContext();
        Assert.False(await db.Roles.AnyAsync(x => x.Id == TestData.Roles.ContentDeveloper, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Role>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Delete.Command { Id = TestData.Roles.ContentDeveloper }));
    }
}
