// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoFixture;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;

namespace Player.Api.Tests.Shared.Fixtures;

/// <summary>
/// AutoFixture customization that registers factories for all Player entity types,
/// avoiding circular reference issues from EF navigation properties.
/// </summary>
public class PlayerCustomization : ICustomization
{
    public void Customize(IFixture fixture)
    {
        // Prevent infinite recursion from navigation properties
        fixture.Behaviors.OfType<ThrowingRecursionBehavior>()
            .ToList()
            .ForEach(b => fixture.Behaviors.Remove(b));
        fixture.Behaviors.Add(new OmitOnRecursionBehavior());

        fixture.Customize<ViewEntity>(c => c
            .Without(x => x.ParentView)
            .Without(x => x.DefaultTeam)
            .Without(x => x.Teams)
            .Without(x => x.Applications)
            .Without(x => x.Memberships)
            .Without(x => x.Files)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.Status, ViewStatus.Active));

        fixture.Customize<TeamEntity>(c => c
            .Without(x => x.View)
            .Without(x => x.Role)
            .Without(x => x.Applications)
            .Without(x => x.Memberships)
            .Without(x => x.Permissions)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.ViewId, () => Guid.NewGuid())
            .With(x => x.RoleId, () => Guid.NewGuid()));

        fixture.Customize<UserEntity>(c => c
            .Without(x => x.Role)
            .Without(x => x.ViewMemberships)
            .Without(x => x.TeamMemberships)
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<TeamMembershipEntity>(c => c
            .Without(x => x.Team)
            .Without(x => x.User)
            .Without(x => x.ViewMembership)
            .Without(x => x.Role)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.TeamId, () => Guid.NewGuid())
            .With(x => x.UserId, () => Guid.NewGuid())
            .With(x => x.ViewMembershipId, () => Guid.NewGuid()));

        fixture.Customize<ViewMembershipEntity>(c => c
            .Without(x => x.View)
            .Without(x => x.User)
            .Without(x => x.PrimaryTeamMembership)
            .Without(x => x.TeamMemberships)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.ViewId, () => Guid.NewGuid())
            .With(x => x.UserId, () => Guid.NewGuid()));

        fixture.Customize<ApplicationTemplateEntity>(c => c
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<ApplicationEntity>(c => c
            .Without(x => x.View)
            .Without(x => x.Template)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.ViewId, () => Guid.NewGuid()));

        fixture.Customize<ApplicationInstanceEntity>(c => c
            .Without(x => x.Team)
            .Without(x => x.Application)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.TeamId, () => Guid.NewGuid())
            .With(x => x.ApplicationId, () => Guid.NewGuid()));

        fixture.Customize<FileEntity>(c => c
            .Without(x => x.View)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.TeamIds, () => new List<Guid> { Guid.NewGuid() }));

        fixture.Customize<NotificationEntity>(c => c
            .With(x => x.BroadcastTime, () => DateTime.UtcNow)
            .With(x => x.Priority, NotificationPriority.Normal)
            .With(x => x.FromType, NotificationType.User)
            .With(x => x.ToType, NotificationType.Team));

        fixture.Customize<RoleEntity>(c => c
            .Without(x => x.Permissions)
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<PermissionEntity>(c => c
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<RolePermissionEntity>(c => c
            .Without(x => x.Role)
            .Without(x => x.Permission)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.RoleId, () => Guid.NewGuid())
            .With(x => x.PermissionId, () => Guid.NewGuid()));

        fixture.Customize<TeamRoleEntity>(c => c
            .Without(x => x.Permissions)
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<TeamPermissionEntity>(c => c
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<TeamPermissionAssignmentEntity>(c => c
            .Without(x => x.Team)
            .Without(x => x.Permission)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.TeamId, () => Guid.NewGuid())
            .With(x => x.PermissionId, () => Guid.NewGuid()));

        fixture.Customize<TeamRolePermissionEntity>(c => c
            .Without(x => x.Role)
            .Without(x => x.Permission)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.RoleId, () => Guid.NewGuid())
            .With(x => x.PermissionId, () => Guid.NewGuid()));

        fixture.Customize<WebhookSubscriptionEntity>(c => c
            .Without(x => x.EventTypes)
            .With(x => x.Id, () => Guid.NewGuid()));

        fixture.Customize<WebhookSubscriptionEventTypeEntity>(c => c
            .Without(x => x.Subscription)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.SubscriptionId, () => Guid.NewGuid())
            .With(x => x.EventType, EventType.ViewCreated));

        fixture.Customize<PendingEventEntity>(c => c
            .Without(x => x.Subscription)
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.SubscriptionId, () => Guid.NewGuid())
            .With(x => x.Timestamp, () => DateTime.UtcNow)
            .With(x => x.EventType, EventType.ViewCreated));

        fixture.Customize<UserDeletionAuditLogEntity>(c => c
            .With(x => x.Id, () => Guid.NewGuid())
            .With(x => x.UserId, () => Guid.NewGuid())
            .With(x => x.InitiatedBy, () => Guid.NewGuid())
            .With(x => x.InitiatedAt, () => DateTime.UtcNow)
            .With(x => x.Status, UserDeletionStatus.Pending));
    }
}
