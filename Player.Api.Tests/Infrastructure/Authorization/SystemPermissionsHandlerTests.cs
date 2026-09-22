// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authorization;

public class SystemPermissionsHandlerTests
{
    private readonly SystemPermissionsHandler _handler = new();

    [Fact]
    public async Task Fails_when_there_is_no_user()
    {
        var requirement = new SystemPermissionRequirement([SystemPermission.ViewViews]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user: null);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_the_user_holds_a_required_permission()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewViews)
            .Build();
        var requirement = new SystemPermissionRequirement([SystemPermission.ViewViews]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_the_user_holds_any_one_of_the_required_permissions()
    {
        // The required permissions are alternatives, not a conjunction.
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ManageViews)
            .Build();
        var requirement = new SystemPermissionRequirement(
            [SystemPermission.ViewViews, SystemPermission.ManageViews]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_the_user_holds_none_of_the_required_permissions()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewUsers)
            .Build();
        var requirement = new SystemPermissionRequirement([SystemPermission.ManageUsers]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Ignores_team_permission_claims()
    {
        // Team claims must not leak into system-permission decisions, even when the names overlap.
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(Guid.NewGuid(), Guid.NewGuid(), viewPermissions: [ViewPermission.ManageView])
            .Build();
        var requirement = new SystemPermissionRequirement([SystemPermission.ManageViews]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.False(context.HasSucceeded);
    }

    /// <summary>
    /// An empty or null requirement means "authenticated is enough". This is load-bearing:
    /// <c>AuthorizationService</c> reaches the team requirement only when the system requirement did
    /// not succeed, so if this granted nothing the team check would run for every request, and if it
    /// granted too much the team check would never run at all.
    /// </summary>
    [Fact]
    public async Task Succeeds_when_no_permissions_are_required()
    {
        var user = ClaimsPrincipalBuilder.Anonymous();
        var requirement = new SystemPermissionRequirement([]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_the_required_permissions_are_null()
    {
        var user = ClaimsPrincipalBuilder.Anonymous();
        var requirement = new SystemPermissionRequirement(null);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }
}
