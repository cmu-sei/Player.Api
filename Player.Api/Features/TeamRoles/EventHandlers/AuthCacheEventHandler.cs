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

namespace Player.Api.Features.TeamRoles.EventHandlers;

public class TeamRoleBaseAuthCacheHandler(IMemoryCache cache, PlayerContext dbContext)
{
    protected async Task UpdateCache(Guid roleId)
    {
        var userIds = await dbContext.TeamMemberships
                    .Where(x => x.RoleId == roleId)
                    .Select(x => x.UserId)
                    .ToListAsync();

        foreach (var userId in userIds)
        {
            cache.Remove(userId);
        }
    }
}

public class TeamRoleUpdatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamRoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityUpdated<TeamRoleEntity>>
{
    public async Task Handle(EntityUpdated<TeamRoleEntity> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Any(x =>
            x == nameof(TeamRoleEntity.AllPermissions)))
        {
            await UpdateCache(notification.Entity.Id);
        }
    }
}

public class TeamRoleDeletedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamRoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<TeamRoleEntity>>
{
    public async Task Handle(EntityDeleted<TeamRoleEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.Id);
    }
}

public class TeamRolePermissionCreatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamRoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityCreated<TeamRolePermissionEntity>>
{
    public async Task Handle(EntityCreated<TeamRolePermissionEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.RoleId);
    }
}

public class TeamRolePermissioDeletedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : TeamRoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<TeamRolePermissionEntity>>
{
    public async Task Handle(EntityDeleted<TeamRolePermissionEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.RoleId);
    }
}