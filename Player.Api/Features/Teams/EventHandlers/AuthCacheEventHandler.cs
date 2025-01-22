// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Events;

namespace Player.Api.Features.Teams.EventHandlers;

public class TeamBaseAuthCacheHandler(IMemoryCache cache, PlayerContext dbContext)
{
    protected async Task UpdateCache(Guid teamId)
    {
        var userIds = await dbContext.TeamMemberships
                    .Where(x => x.TeamId == teamId)
                    .Select(x => x.UserId)
                    .ToListAsync();

        foreach (var userId in userIds)
        {
            cache.Remove(userId);
        }
    }
}

public class TeamUpdatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityUpdated<TeamEntity>>
{
    public async Task Handle(EntityUpdated<TeamEntity> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Any(x =>
            x == nameof(TeamEntity.RoleId)))
        {
            await UpdateCache(notification.Entity.Id);
        }
    }
}

public class TeamPermissionAssignmentCreatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityCreated<TeamPermissionAssignmentEntity>>
{
    public async Task Handle(EntityCreated<TeamPermissionAssignmentEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.TeamId);
    }
}

public class TeamPermissionAssignmentDeletedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<TeamPermissionAssignmentEntity>>
{
    public async Task Handle(EntityDeleted<TeamPermissionAssignmentEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.TeamId);
    }
}