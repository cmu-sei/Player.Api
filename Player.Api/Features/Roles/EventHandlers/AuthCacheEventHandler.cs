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

namespace Player.Api.Features.Roles.EventHandlers;

public class RoleBaseAuthCacheHandler(IMemoryCache cache, PlayerContext dbContext)
{
    protected async Task UpdateCache(Guid roleId)
    {
        var userIds = await dbContext.Users
                    .Where(x => x.RoleId == roleId)
                    .Select(x => x.Id)
                    .ToListAsync();

        foreach (var userId in userIds)
        {
            cache.Remove(userId);
        }
    }
}

public class RoleUpdatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : RoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityUpdated<RoleEntity>>
{
    public async Task Handle(EntityUpdated<RoleEntity> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Any(x =>
            x == nameof(RoleEntity.AllPermissions)))
        {
            await UpdateCache(notification.Entity.Id);
        }
    }
}

public class RoleDeletedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : RoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<RoleEntity>>
{
    public async Task Handle(EntityDeleted<RoleEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.Id);
    }
}

public class RolePermissionCreatedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : RoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityCreated<RolePermissionEntity>>
{
    public async Task Handle(EntityCreated<RolePermissionEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.RoleId);
    }
}

public class RolePermissioDeletedAuthCacheHandler(IMemoryCache memoryCache, PlayerContext db) : RoleBaseAuthCacheHandler(memoryCache, db),
    INotificationHandler<EntityDeleted<RolePermissionEntity>>
{
    public async Task Handle(EntityDeleted<RolePermissionEntity> notification, CancellationToken cancellationToken)
    {
        await UpdateCache(notification.Entity.RoleId);
    }
}