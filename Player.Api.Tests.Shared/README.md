# Player.Api.Tests.Shared

Shared test fixtures and utilities for Player API testing.

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

## Purpose

This project provides reusable test infrastructure shared across Player API unit and integration tests. Player is the main exercise interface in the Crucible framework, managing teams, views, users, and application embedding for cybersecurity exercises.

## Files

### Fixtures/PlayerCustomization.cs

AutoFixture customization that registers factories for all Player entity types, preventing circular reference issues from Entity Framework navigation properties. Configures entity builders with:

- Omitted navigation properties (View.Teams, Team.Memberships, User.ViewMemberships, etc.)
- Generated GUIDs for primary keys
- Default enum values (ViewStatus.Active, NotificationPriority.Normal, etc.)
- Empty collections where needed (FileEntity.TeamIds)

Entities supported:
- `ViewEntity`, `TeamEntity`, `UserEntity`
- `TeamMembershipEntity`, `ViewMembershipEntity`
- `ApplicationEntity`, `ApplicationTemplateEntity`, `ApplicationInstanceEntity`
- `FileEntity`, `NotificationEntity`
- `RoleEntity`, `PermissionEntity`, `RolePermissionEntity`
- `TeamRoleEntity`, `TeamPermissionEntity`, `TeamPermissionAssignmentEntity`, `TeamRolePermissionEntity`
- `WebhookSubscriptionEntity`, `WebhookSubscriptionEventTypeEntity`, `PendingEventEntity`
- `UserDeletionAuditLogEntity`

## Important Notes

Player API uses the `Player.Api.Data.Data` namespace (note the double "Data" segment) for entity models.

## Usage

Reference this project from unit or integration test projects:

```xml
<ProjectReference Include="..\Player.Api.Tests.Shared\Player.Api.Tests.Shared.csproj" />
```

Apply the customization in test setup:

```csharp
using Player.Api.Tests.Shared.Fixtures;

var fixture = new Fixture().Customize(new PlayerCustomization());
var view = fixture.Create<ViewEntity>(); // No circular reference issues
```

This customization is used across both unit and integration tests to generate test data with AutoFixture.

## Dependencies

- Player.Api
- Player.Api.Data
- Crucible.Common.Testing
- AutoFixture (via consuming test projects)
