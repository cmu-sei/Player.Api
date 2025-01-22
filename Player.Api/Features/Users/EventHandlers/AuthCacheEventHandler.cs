// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data.Models;
using Player.Api.Events;

namespace Player.Api.Features.Users.EventHandlers;

public class UserUpdatedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityUpdated<UserEntity>>
{
    public Task Handle(EntityUpdated<UserEntity> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Any(x => x == nameof(UserEntity.RoleId)))
        {
            cache.Remove(notification.Entity.Id);
        }

        return Task.CompletedTask;
    }
}

public class UserDeletedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityDeleted<UserEntity>>
{
    public Task Handle(EntityDeleted<UserEntity> notification, CancellationToken cancellationToken)
    {
        cache.Remove(notification.Entity.Id);
        return Task.CompletedTask;
    }
}