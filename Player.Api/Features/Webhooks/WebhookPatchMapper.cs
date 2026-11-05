// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.ViewModels.Webhooks;
using Riok.Mapperly.Abstractions;

namespace Player.Api.Features.Webhooks;

/// <summary>
/// The PATCH update. Every null in the form is skipped, so an omitted member — including
/// <see cref="WebhookSubscriptionForm.EventTypes"/> — keeps its current value.
/// </summary>
[Mapper(AllowNullPropertyAssignment = false)]
public static partial class WebhookPatchMapper
{
    public static void ApplyPatchTo(this WebhookSubscriptionPartialEditForm source, WebhookSubscriptionEntity target)
    {
        ApplyNonNullMembers(source, target);

        // Guarded by hand: AllowNullPropertyAssignment does not apply to a member mapped through a
        // user method, which would turn an omitted list into "no event types".
        if (source.EventTypes != null)
            target.EventTypes = WebhookMapper.ToEventTypeEntities(source.EventTypes);
    }

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(WebhookSubscriptionForm.EventTypes))]
    private static partial void ApplyNonNullMembers(WebhookSubscriptionPartialEditForm source, WebhookSubscriptionEntity target);
}
