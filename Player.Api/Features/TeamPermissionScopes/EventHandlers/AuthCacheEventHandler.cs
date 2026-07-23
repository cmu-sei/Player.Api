// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;

namespace Player.Api.Features.TeamPermissionScopes.EventHandlers;

public class TeamPermissionScopeBaseAuthCacheHandler(IMemoryCache cache, PlayerContext dbContext)
{
    protected async Task UpdateCache(Guid teamId, CancellationToken cancellationToken)
    {
        var userIds = await dbContext.TeamMemberships
            .Where(x => x.TeamId == teamId)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            cache.Remove(userId);
        }
    }
}

public class TeamPermissionScopeCreatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) :
    TeamPermissionScopeBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityCreated<TeamPermissionScopeEntity>>
{
    public async Task Handle(EntityCreated<TeamPermissionScopeEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.TeamId, cancellationToken);
    }
}

public class TeamPermissionScopeDeletedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) :
    TeamPermissionScopeBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<TeamPermissionScopeEntity>>
{
    public async Task Handle(EntityDeleted<TeamPermissionScopeEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.TeamId, cancellationToken);
    }
}
