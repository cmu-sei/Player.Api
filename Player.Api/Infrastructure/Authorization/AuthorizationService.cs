/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved.
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;

namespace Player.Api.Infrastructure.Authorization;

public interface IPlayerAuthorizationService
{
    Task<bool> Authorize(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken);

    Task<bool> Authorize(
        SystemPermission[] requiredSystemPermissions,
        ViewPermission[] requiredViewPermissions,
        TeamPermission[] requiredTeamPermissions,
        CancellationToken cancellationToken);

    Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        ViewPermission[] requiredViewPermissions,
        TeamPermission[] requiredTeamPermissions,
        CancellationToken cancellationToken) where T : IEntity;

    IEnumerable<Guid> GetAuthorizedViewIds();
    IEnumerable<string> GetSystemPermissions();
    IEnumerable<TeamPermissionsClaim> GetTeamPermissions();
    IEnumerable<Guid> GetVisibleTeamIds(Guid viewId);
    Task<PrimaryVisibilityContext> GetPrimaryVisibilityContext(Guid viewId, CancellationToken cancellationToken);
    bool IsCurrentUser(Guid userId);
}

public class AuthorizationService(
    IAuthorizationService authService,
    IIdentityResolver identityResolver,
    PlayerContext dbContext) : IPlayerAuthorizationService
{
    public async Task<bool> Authorize(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken)
    {
        return await Authorize<IEntity>(null, requiredSystemPermissions, null, null, cancellationToken);
    }

    public async Task<bool> Authorize(
        SystemPermission[] requiredSystemPermissions,
        ViewPermission[] requiredViewPermissions,
        TeamPermission[] requiredTeamPermissions,
        CancellationToken cancellationToken)
    {
        return await Authorize<IEntity>(null, requiredSystemPermissions, requiredViewPermissions, requiredTeamPermissions, cancellationToken);
    }

    public async Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        ViewPermission[] requiredViewPermissions,
        TeamPermission[] requiredTeamPermissions,
        CancellationToken cancellationToken) where T : IEntity
    {
        var succeeded = false;
        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        var permissionRequirement = new SystemPermissionRequirement(requiredSystemPermissions);
        var permissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, permissionRequirement);

        if (permissionResult.Succeeded)
            succeeded = true;

        if (!succeeded)
        {
            TeamPermissionRequirement teamPermissionRequirement;

            if (resourceId.HasValue)
            {
                var result = await GetResourceResult<T>(resourceId.Value, cancellationToken);

                if (result == null)
                {
                    return false;
                }
                else
                {
                    teamPermissionRequirement = new TeamPermissionRequirement(requiredViewPermissions, requiredTeamPermissions, result.ViewId, result.TeamId);
                }
            }
            else
            {
                // Check for required permissions in ANY view or team
                teamPermissionRequirement = new TeamPermissionRequirement(requiredViewPermissions, requiredTeamPermissions, null, null);
            }

            permissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, teamPermissionRequirement);
            succeeded = permissionResult.Succeeded;
        }

        return succeeded;
    }

    public bool IsCurrentUser(Guid userId)
    {
        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        var currentUserId = claimsPrincipal.GetId();

        return currentUserId == userId;
    }

    public IEnumerable<Guid> GetAuthorizedViewIds()
    {
        return identityResolver.GetClaimsPrincipal().Claims
            .Where(x => x.Type == AuthorizationConstants.TeamPermissionsClaimType)
            .Select(x => TeamPermissionsClaim.FromString(x.Value).ViewId)
            .ToList();
    }

    public IEnumerable<string> GetSystemPermissions()
    {
        return identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.PermissionsClaimType)
           .Select(x => x.Value)
           .ToList();
    }

    public IEnumerable<TeamPermissionsClaim> GetTeamPermissions()
    {
        var permissions = identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.TeamPermissionsClaimType)
           .Select(x => TeamPermissionsClaim.FromString(x.Value));

        return permissions;
    }

    // Team ids within a View that the current user can see by team-level permission: the
    // team-level analog of ViewView/ManageView. Includes the user's member teams and any
    // teams scoped to them whose resolved claim grants ViewTeam or ManageTeam. Used to
    // surface scoped teams in "my teams in this View" results without altering the
    // ViewView/ManageView all-teams behavior.
    public IEnumerable<Guid> GetVisibleTeamIds(Guid viewId)
    {
        return GetTeamPermissions()
            .Where(x => x.ViewId == viewId &&
                (x.TeamPermissions.Contains(TeamPermission.ViewTeam) ||
                 x.TeamPermissions.Contains(TeamPermission.ManageTeam)))
            .Select(x => x.TeamId)
            .Distinct()
            .ToList();
    }

    public async Task<PrimaryVisibilityContext> GetPrimaryVisibilityContext(
        Guid viewId,
        CancellationToken cancellationToken)
    {
        var viewClaims = GetTeamPermissions()
            .Where(x => x.ViewId == viewId)
            .ToArray();
        var primaryClaim = viewClaims.FirstOrDefault(x => x.IsPrimary);

        if (primaryClaim == null)
            return PrimaryVisibilityContext.Empty;

        var canViewAllTeams =
            primaryClaim.DirectViewPermissions.Contains(ViewPermission.ViewView) ||
            primaryClaim.DirectViewPermissions.Contains(ViewPermission.ManageView);

        if (canViewAllTeams)
        {
            var allTeamIds = await dbContext.Teams
                .Where(x => x.ViewId == viewId)
                .Select(x => x.Id)
                .ToHashSetAsync(cancellationToken);

            return new PrimaryVisibilityContext(primaryClaim.TeamId, true, allTeamIds);
        }

        var visibleTeamIds = new HashSet<Guid> { primaryClaim.TeamId };

        if (primaryClaim.DirectTeamPermissions.Contains(TeamPermission.ViewTeam) ||
            primaryClaim.DirectTeamPermissions.Contains(TeamPermission.ManageTeam))
        {
            visibleTeamIds.UnionWith(viewClaims
                .Where(x => x.SourceTeamIds?.Contains(primaryClaim.TeamId) ?? false)
                .Select(x => x.TeamId));
        }

        return new PrimaryVisibilityContext(primaryClaim.TeamId, false, visibleTeamIds);
    }

    private async Task<ResourceResult> GetResourceResult<T>(Guid resourceId, CancellationToken cancellationToken)
    {
        return typeof(T) switch
        {
            var t when t == typeof(ViewEntity) => new ResourceResult { ViewId = resourceId },
            var t when t == typeof(TeamEntity) => await HandleTeam(resourceId, cancellationToken),
            var t when t == typeof(ApplicationEntity) => await HandleApplication(resourceId, cancellationToken),
            var t when t == typeof(ApplicationInstanceEntity) => await HandleApplicationInstance(resourceId, cancellationToken),
            var t when t == typeof(TeamMembershipEntity) => await HandleTeamMembership(resourceId, cancellationToken),
            var t when t == typeof(ViewMembershipEntity) => await HandleViewMembership(resourceId, cancellationToken),
            _ => throw new NotImplementedException($"Handler for type {typeof(T).Name} is not implemented.")
        };
    }

    private async Task<ResourceResult> HandleTeam(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Teams
            .Where(x => x.Id == id)
            .Select(x => new ResourceResult
            {
                ViewId = x.ViewId,
                TeamId = x.Id
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ResourceResult> HandleApplication(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Applications
            .Where(x => x.Id == id)
            .Select(x => new ResourceResult
            {
                ViewId = x.ViewId
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ResourceResult> HandleApplicationInstance(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.ApplicationInstances
            .Where(x => x.Id == id)
            .Select(x => new ResourceResult
            {
                ViewId = x.Team.ViewId,
                TeamId = x.TeamId
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ResourceResult> HandleTeamMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.TeamMemberships
            .Where(x => x.Id == id)
            .Select(x => new ResourceResult
            {
                ViewId = x.Team.ViewId,
                TeamId = x.TeamId
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ResourceResult> HandleViewMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.ViewMemberships
            .Where(x => x.Id == id)
            .Select(x => new ResourceResult
            {
                ViewId = x.ViewId,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private class ResourceResult
    {
        public Guid ViewId { get; set; }
        public Guid? TeamId { get; set; }
    }
}

public sealed record PrimaryVisibilityContext(
    Guid? PrimaryTeamId,
    bool CanViewAllTeams,
    IReadOnlySet<Guid> TeamIds)
{
    public static PrimaryVisibilityContext Empty { get; } = new(null, false, new HashSet<Guid>());
}
