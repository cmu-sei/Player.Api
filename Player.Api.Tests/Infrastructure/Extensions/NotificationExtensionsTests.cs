// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Extensions;

namespace Player.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// The SignalR group names, which are a contract between the hubs that join them and the notification
/// service that broadcasts to them.
/// </summary>
public class NotificationExtensionsTests
{
    [Fact]
    public void Broadcast_groups_are_named_by_scope_and_id()
    {
        var id = Guid.NewGuid();

        Assert.Equal($"View_{id}", NotificationExtensions.ViewBroadcastGroup(id));
        Assert.Equal($"Team_{id}", NotificationExtensions.TeamBroadcastGroup(id));
        Assert.Equal($"User_{id}", NotificationExtensions.UserBroadcastGroup(id));
    }

    /// <summary>
    /// The scope prefix is what keeps a view, a team and a user sharing an id from sharing a group.
    /// </summary>
    [Fact]
    public void Broadcast_groups_for_one_id_are_distinct()
    {
        var id = Guid.NewGuid();

        Assert.Equal(3, new HashSet<string>
        {
            NotificationExtensions.ViewBroadcastGroup(id),
            NotificationExtensions.TeamBroadcastGroup(id),
            NotificationExtensions.UserBroadcastGroup(id)
        }.Count);
    }
}
