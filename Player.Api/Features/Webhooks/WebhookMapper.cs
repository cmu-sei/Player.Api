// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.ViewModels.Webhooks;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Webhooks;

/// <summary>The Webhooks feature's mappings, apart from PATCH, which is <see cref="WebhookPatchMapper"/>.</summary>
[Mapper]
public static partial class WebhookMapper
{
    // ---- Entity to view model -------------------------------------------------------------------

    public static partial IQueryable<WebhookSubscription> ProjectToWebhookSubscription(this IQueryable<WebhookSubscriptionEntity> query);

    [MapProperty(nameof(WebhookSubscriptionEntity.EventTypes), nameof(WebhookSubscription.EventTypes), Use = nameof(MapEventTypes))]
    [MapperIgnoreTarget(nameof(WebhookSubscription.ClientSecretSet))]
    public static partial WebhookSubscription ToWebhookSubscription(this WebhookSubscriptionEntity source);

    // Expression-bodied, so Mapperly can inline it into the projection rather than call it.
    private static List<EventType> MapEventTypes(ICollection<WebhookSubscriptionEventTypeEntity> eventTypes) =>
        eventTypes.Select(x => x.EventType).ToList();

    // ---- Form to entity -------------------------------------------------------------------------

    /// <summary>A new subscription from the create command, which is a <see cref="WebhookSubscriptionForm"/>.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapProperty(nameof(WebhookSubscriptionForm.EventTypes), nameof(WebhookSubscriptionEntity.EventTypes), Use = nameof(ToEventTypeEntities))]
    public static partial WebhookSubscriptionEntity ToEntity(this WebhookSubscriptionForm source);

    /// <summary>
    /// The PUT update: every member is written, so a null clears it, and a null
    /// <see cref="WebhookSubscriptionForm.EventTypes"/> removes every event type.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapProperty(nameof(WebhookSubscriptionForm.EventTypes), nameof(WebhookSubscriptionEntity.EventTypes), Use = nameof(ToEventTypeEntities))]
    public static partial void ApplyTo(this WebhookSubscriptionForm source, WebhookSubscriptionEntity target);

    /// <summary>A new subscription from seed data, keeping its id and its secret.</summary>
    /// <remarks>
    /// <see cref="WebhookSubscription.ClientSecret"/> reads back as a mask, so the secret comes from
    /// <see cref="WebhookSubscription.GetClientSecret"/>. <see cref="WebhookSubscription.ClientSecretSet"/>
    /// is derived from it. AutoMapper read the mask instead, so a seeded secret used to be stored empty.
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapPropertyFromSource(nameof(WebhookSubscriptionEntity.ClientSecret), Use = nameof(GetClientSecret))]
    [MapperIgnoreSource(nameof(WebhookSubscription.ClientSecretSet))]
    [MapProperty(nameof(WebhookSubscription.EventTypes), nameof(WebhookSubscriptionEntity.EventTypes), Use = nameof(ToEventTypeEntities))]
    public static partial WebhookSubscriptionEntity ToEntity(this WebhookSubscription source);

    private static string GetClientSecret(WebhookSubscription source) => source.GetClientSecret();

    /// <summary>
    /// The event type rows for a list of event types. The subscription id is left empty for EF Core to
    /// fill from the relationship. A null list maps to no event types, as it did through AutoMapper.
    /// </summary>
    internal static ICollection<WebhookSubscriptionEventTypeEntity> ToEventTypeEntities(List<EventType> eventTypes) =>
        eventTypes?.Select(x => new WebhookSubscriptionEventTypeEntity(Guid.Empty, x)).ToList() ?? [];
}
