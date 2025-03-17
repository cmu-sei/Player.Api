// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Events;

namespace Player.Api.Features.TeamMemberships.EventHandlers;

public class TeamMembershipBaseAuthCacheHandler(IMemoryCache cache)
{
    protected void UpdateCache(TeamMembershipEntity membership)
    {
        cache.Remove(membership.UserId);
    }
}

public class TeamMembershipCreatedAuthCacheHandler(IMemoryCache memoryCache) : TeamMembershipBaseAuthCacheHandler(memoryCache),
    INotificationHandler<EntityCreated<TeamMembershipEntity>>
{
    public Task Handle(EntityCreated<TeamMembershipEntity> notification, CancellationToken cancellationToken)
    {
        UpdateCache(notification.Entity);
        return Task.CompletedTask;
    }
}

public class TeamMembershipUpdatedAuthCacheHandler(IMemoryCache memoryCache) : TeamMembershipBaseAuthCacheHandler(memoryCache),
    INotificationHandler<EntityUpdated<TeamMembershipEntity>>
{
    public Task Handle(EntityUpdated<TeamMembershipEntity> notification, CancellationToken cancellationToken)
    {
        UpdateCache(notification.Entity);
        return Task.CompletedTask;
    }
}

public class TeamMembershipDeletedAuthCacheHandler(IMemoryCache memoryCache) : TeamMembershipBaseAuthCacheHandler(memoryCache),
    INotificationHandler<EntityDeleted<TeamMembershipEntity>>
{
    public Task Handle(EntityDeleted<TeamMembershipEntity> notification, CancellationToken cancellationToken)
    {
        UpdateCache(notification.Entity);
        return Task.CompletedTask;
    }
}