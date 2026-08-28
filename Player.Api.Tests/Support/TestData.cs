// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;

namespace Player.Api.Tests.Support;

/// <summary>
/// Object mothers for the entities permission tests need.
/// </summary>
/// <remarks>
/// Every mother assigns an explicit <see cref="Guid.NewGuid"/> id. This is deliberate: on
/// PostgreSQL ids are store-generated (<c>uuid_generate_v4()</c>, via
/// <c>ModelBuilderExtensions.AddPostgresUUIDGeneration</c>) while on the SQLite fallback nothing
/// generates them, so an unassigned id would come back as <see cref="Guid.Empty"/> there and
/// collide on the second insert. Assigning up front makes a test's ids identical under both
/// providers, and lets a test hold an id before saving.
/// </remarks>
public static class TestData
{
    /// <summary>
    /// Ids of the <see cref="TeamRoleEntity"/> rows seeded by
    /// <c>PlayerContext.SeedTeamRoles</c>. <see cref="TeamEntity.RoleId"/> is a required foreign
    /// key, so every team needs one of these — the seed data is present under both providers
    /// (migrations on PostgreSQL, <c>EnsureCreated</c> on SQLite).
    /// </summary>
    public static class TeamRoles
    {
        public static readonly Guid ViewAdmin = new("b65ce1b0-f995-45e1-93fc-47a09542cee5");
        public static readonly Guid Observer = new("c875dcce-2488-4e73-8585-8375b4730151");
        public static readonly Guid ViewMember = new("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22");
    }

    /// <summary>
    /// Ids of the <see cref="RoleEntity"/> rows seeded by <c>PlayerContext.SeedRoles</c>.
    /// </summary>
    public static class Roles
    {
        public static readonly Guid Administrator = new("f6c07d62-4f2c-4bd5-82af-bf32c0daccc7");

        /// <summary>The only seeded role that is not immutable.</summary>
        public static readonly Guid ContentDeveloper = new("7fd6aa3e-a765-47b8-a77e-f58eae53a82f");
    }

    /// <summary>
    /// Ids of <see cref="PermissionEntity"/> rows seeded by <c>PlayerContext.SeedRoles</c>, one of each
    /// mutability — the handlers refuse to edit or delete an immutable permission.
    /// </summary>
    public static class Permissions
    {
        public static readonly Guid CreateViews = new("06e2699d-21a9-4053-922a-411499b3e923");
        public static readonly Guid ViewNetworks = new("df487ab3-e4d2-4879-8ba6-be626b5df5bc");
    }

    /// <summary>
    /// Ids of <see cref="TeamPermissionEntity"/> rows seeded by <c>PlayerContext.SeedTeamRoles</c>, one
    /// of each mutability.
    /// </summary>
    public static class TeamPermissions
    {
        public static readonly Guid ViewTeam = new("f3ef9465-7f7c-43ef-9855-83798ce5bcd5");
        public static readonly Guid UploadViewIsos = new("5da3014c-a6a5-4c3c-a658-e86672801313");
    }

    /// <summary>
    /// A fixed creation timestamp. Tests that care about ordering pass their own.
    /// </summary>
    public static readonly DateTime DefaultDateCreated = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ViewEntity View(string name = "Test View", ViewStatus status = ViewStatus.Active) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = status,
            DateCreated = DefaultDateCreated
        };

    public static TeamEntity Team(Guid viewId, string name = "Test Team", Guid? roleId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            ViewId = viewId,
            RoleId = roleId ?? TeamRoles.ViewMember
        };

    /// <summary>
    /// A system role granting nothing. Add <see cref="RolePermissionEntity"/> rows, or set
    /// <paramref name="allPermissions"/>, to make it grant something.
    /// </summary>
    /// <remarks>
    /// The default name carries a Guid because role names are unique and the seeded
    /// <c>Administrator</c> and <c>Content Developer</c> rows are already there.
    /// </remarks>
    public static RoleEntity Role(string name = null, bool allPermissions = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"Test Role {Guid.NewGuid():N}",
            AllPermissions = allPermissions
        };

    /// <summary>
    /// A team role granting nothing, for the team that must grant its members no permissions —
    /// <see cref="Team"/> defaults to the seeded <c>View Member</c> role, which grants four.
    /// </summary>
    public static TeamRoleEntity TeamRole(string name = null, bool allPermissions = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"Test Team Role {Guid.NewGuid():N}",
            AllPermissions = allPermissions
        };

    /// <summary>
    /// A user. <see cref="UserEntity.Key"/> is an <c>int</c> identity column and is left unset so
    /// the store assigns it; <see cref="UserEntity.Id"/> is the Guid the rest of the domain keys off.
    /// </summary>
    public static UserEntity User(Guid? id = null, string name = "Test User", Guid? roleId = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            RoleId = roleId
        };

    public static ViewMembershipEntity ViewMembership(Guid viewId, Guid userId) =>
        new(viewId, userId) { Id = Guid.NewGuid() };

    public static TeamMembershipEntity TeamMembership(
        Guid teamId,
        Guid userId,
        Guid viewMembershipId,
        Guid? roleId = null) =>
        new(teamId, userId)
        {
            Id = Guid.NewGuid(),
            ViewMembershipId = viewMembershipId,
            RoleId = roleId
        };

    public static ApplicationTemplateEntity ApplicationTemplate(
        string name = "Test Template",
        string url = "https://example.test/app",
        string icon = "https://example.test/icon.png",
        bool embeddable = true,
        bool loadInBackground = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            Icon = icon,
            Embeddable = embeddable,
            LoadInBackground = loadInBackground
        };

    /// <summary>
    /// An application in a view. Every display property is nullable, because an application backed by a
    /// template falls back to the template's value for each one it leaves unset.
    /// </summary>
    public static ApplicationEntity Application(
        Guid viewId,
        string name = "Test Application",
        string url = "https://example.test/app",
        Guid? templateId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            ViewId = viewId,
            ApplicationTemplateId = templateId
        };

    public static ApplicationInstanceEntity ApplicationInstance(
        Guid teamId,
        Guid applicationId,
        float displayOrder = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            ApplicationId = applicationId,
            DisplayOrder = displayOrder
        };

    /// <summary>
    /// Scopes <paramref name="teamId"/>'s effective team permissions onto
    /// <paramref name="targetTeamId"/> — the relationship that makes a claim's effective and direct
    /// permissions diverge.
    /// </summary>
    public static TeamPermissionScopeEntity TeamPermissionScope(Guid teamId, Guid targetTeamId) =>
        new(teamId, targetTeamId) { Id = Guid.NewGuid() };

    /// <summary>
    /// A notification. <see cref="NotificationEntity.Key"/> is an <c>int</c> identity column and is left
    /// unset so the store assigns it.
    /// </summary>
    public static NotificationEntity Notification(
        Guid toId,
        NotificationType toType = NotificationType.View,
        string text = "Test notification",
        NotificationPriority priority = NotificationPriority.Normal,
        Guid? viewId = null,
        DateTime? broadcastTime = null) =>
        new()
        {
            ViewId = viewId,
            ToId = toId,
            ToType = toType,
            ToName = "Test Recipient",
            FromId = Guid.NewGuid(),
            FromType = NotificationType.User,
            FromName = "Test Sender",
            Subject = "Test Subject",
            Text = text,
            Priority = priority,
            BroadcastTime = broadcastTime ?? DefaultDateCreated
        };

    public static WebhookSubscriptionEntity Webhook(
        string name = "Test Webhook",
        string callbackUri = "https://example.test/hook",
        EventType[] eventTypes = null)
    {
        var entity = new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            CallbackUri = callbackUri,
            ClientId = "test-client",
            ClientSecret = "test-secret"
        };

        foreach (var eventType in eventTypes ?? [])
        {
            entity.EventTypes.Add(new WebhookSubscriptionEventTypeEntity(entity.Id, eventType)
            {
                Id = Guid.NewGuid()
            });
        }

        return entity;
    }

    /// <summary>
    /// An event waiting to be delivered to one webhook subscription. <paramref name="subscriptionId"/> is a
    /// required foreign key, so it must name a saved <see cref="WebhookSubscriptionEntity"/>.
    /// </summary>
    public static PendingEventEntity PendingEvent(
        Guid subscriptionId,
        EventType eventType = EventType.ViewCreated,
        DateTime? timestamp = null,
        string payload = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            EventType = eventType,
            Timestamp = timestamp ?? DefaultDateCreated,
            Payload = payload ?? """{"Type":0}"""
        };

    /// <summary>
    /// A statement on the xAPI send queue, in whatever state a test needs to find it in. The queue's own
    /// <c>EnqueueAsync</c> overwrites <paramref name="status"/>, <paramref name="queuedAt"/> and
    /// <paramref name="retryCount"/>, so tests seed rows directly to set them.
    /// </summary>
    public static XApiQueuedStatementEntity QueuedStatement(
        XApiQueueStatus status = XApiQueueStatus.Pending,
        DateTime? queuedAt = null,
        int retryCount = 0,
        DateTime? lastAttemptAt = null,
        string verb = "viewed",
        Guid? viewId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            StatementJson = """{"actor":{"objectType":"Agent"}}""",
            Status = status,
            QueuedAt = queuedAt ?? DefaultDateCreated,
            RetryCount = retryCount,
            LastAttemptAt = lastAttemptAt,
            Verb = verb,
            ActivityId = "https://player.test/api/views/test",
            ViewId = viewId
        };
}
