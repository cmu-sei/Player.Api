// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Infrastructure.Mappings;

/// <summary>Notification mappings, used by <c>NotificationService</c>.</summary>
[Mapper]
public static partial class NotificationMapper
{
    /// <summary>
    /// The notification as sent to clients. <see cref="Notification.WasSuccess"/>,
    /// <see cref="Notification.CanPost"/> and <see cref="Notification.IconUrl"/> describe the delivery,
    /// not the stored notification, so the service sets them.
    /// </summary>
    [MapperIgnoreTarget(nameof(Notification.WasSuccess))]
    [MapperIgnoreTarget(nameof(Notification.CanPost))]
    [MapperIgnoreTarget(nameof(Notification.IconUrl))]
    public static partial Notification ToNotification(this NotificationEntity source);

    // Written by hand: with nullable reference types disabled, Mapperly's generated collection mapping
    // does not compile (CS8619 in the generated code).
    public static List<Notification> ToNotifications(this IEnumerable<NotificationEntity> source) =>
        source.Select(x => x.ToNotification()).ToList();

    /// <summary>
    /// A new entity from an incoming notification. The service overwrites the addressing and sender
    /// fields afterwards.
    /// </summary>
    /// <remarks>
    /// <see cref="Notification.Key"/> is copied onto the entity's identity column, as the AutoMapper map
    /// did.
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Notification.WasSuccess))]
    [MapperIgnoreSource(nameof(Notification.CanPost))]
    [MapperIgnoreSource(nameof(Notification.IconUrl))]
    public static partial NotificationEntity ToEntity(this Notification source);
}
