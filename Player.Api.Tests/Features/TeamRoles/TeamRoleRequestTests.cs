// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamRoles;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamRoles;

/// <summary>
/// Covers the <c>TeamRoles</c> feature's request handlers, including the protection the two roles named
/// in <c>RoleOptions</c> get — <c>View Member</c> and <c>View Admin</c> under
/// <see cref="ApiTestHostOptions"/>.
/// </summary>
public class TeamRoleRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Create_persists_the_role()
    {
        var created = await SendAsync(new Create.Command { Name = "Red Team" });

        Assert.Equal("Red Team", created.Name);

        await using var db = NewContext();
        Assert.True(await db.TeamRoles.AnyAsync(x => x.Id == created.Id, Ct));
    }

    [Fact]
    public async Task Create_rejects_a_name_that_is_already_taken()
    {
        await Assert.ThrowsAsync<ConflictException>(
            () => SendAsync(new Create.Command { Name = "View Member" }));
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
            "Observer",
            (await SendAsync(new Get.Query { Id = TestData.TeamRoles.Observer })).Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamRole>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetAll_returns_every_role()
    {
        var roles = await SendAsync(new GetAll.Query());

        Assert.Contains(roles, x => x.Id == TestData.TeamRoles.ViewAdmin);
        Assert.Contains(roles, x => x.Id == TestData.TeamRoles.Observer);
        Assert.Contains(roles, x => x.Id == TestData.TeamRoles.ViewMember);
    }

    /// <summary>
    /// Team administrators need the list to assign roles, so <c>ManageTeam</c> on any team admits the
    /// caller without a system permission.
    /// </summary>
    [Fact]
    public async Task GetAll_is_allowed_for_a_caller_holding_ManageTeam_on_any_team()
    {
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(Guid.NewGuid(), Guid.NewGuid(), teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        Assert.NotEmpty(await SendAsync(caller, new GetAll.Query()));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetAll.Query()));
    }

    [Fact]
    public async Task Edit_renames_the_role()
    {
        var edited = await SendAsync(new Edit.Command
        {
            Id = TestData.TeamRoles.Observer,
            Name = "Watcher"
        });

        Assert.Equal("Watcher", edited.Name);
    }

    /// <summary>
    /// The configured defaults are resolved by name, so renaming one breaks every later create-team and
    /// create-view request.
    /// </summary>
    [Theory]
    [InlineData("View Member")]
    [InlineData("View Admin")]
    public async Task Edit_refuses_to_rename_a_role_named_in_the_options(string name)
    {
        await using var db = NewContext();
        var role = await db.TeamRoles.SingleAsync(x => x.Name == name, Ct);

        await Assert.ThrowsAsync<ConflictException>(
            () => SendAsync(new Edit.Command { Id = role.Id, Name = "Something else" }));
    }

    /// <summary>
    /// Only the name is protected. A default role's other properties stay editable, and the check is
    /// skipped entirely when the name is unchanged.
    /// </summary>
    [Fact]
    public async Task Edit_allows_other_changes_to_a_role_named_in_the_options()
    {
        var edited = await SendAsync(new Edit.Command
        {
            Id = TestData.TeamRoles.ViewMember,
            Name = "View Member",
            AllPermissions = true
        });

        Assert.True(edited.AllPermissions);
    }

    [Fact]
    public async Task Edit_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamRole>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task Delete_removes_the_role()
    {
        await SendAsync(new Delete.Command { Id = TestData.TeamRoles.Observer });

        await using var db = NewContext();
        Assert.False(await db.TeamRoles.AnyAsync(x => x.Id == TestData.TeamRoles.Observer, Ct));
    }

    [Theory]
    [InlineData("View Member")]
    [InlineData("View Admin")]
    public async Task Delete_refuses_a_role_named_in_the_options(string name)
    {
        await using var db = NewContext();
        var role = await db.TeamRoles.SingleAsync(x => x.Name == name, Ct);

        await Assert.ThrowsAsync<ConflictException>(() => SendAsync(new Delete.Command { Id = role.Id }));
    }

    [Fact]
    public async Task Delete_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamRole>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Delete.Command { Id = TestData.TeamRoles.Observer }));
    }
}
