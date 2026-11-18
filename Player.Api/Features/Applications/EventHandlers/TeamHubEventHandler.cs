// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Crucible.Common.EntityEvents.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Hubs;

namespace Player.Api.Features.Applications.EventHandlers;

/// <summary>
/// Tells the members of a team that its list of applications has changed, so that an open Player view can
/// fetch it again. The message carries the team id alone: each client re-reads the list through the
/// application instances endpoint, which applies the caller's own permissions, and only connections that
/// <see cref="TeamHub.Join"/> admitted to the team's group receive it.
/// </summary>
/// <remarks>
/// A failure is logged rather than thrown, so it can't stop a later handler of the same event: the change is
/// already saved, and a client that missed the message fetches the list again on its next change or reconnect.
/// </remarks>
public class ApplicationsBaseTeamHubHandler(
    IHubContext<TeamHub> teamHub,
    PlayerContext dbContext,
    ILogger<ApplicationsBaseTeamHubHandler> logger)
{
    public const string ApplicationsChanged = "ApplicationsChanged";

    protected Task Notify(Guid teamId, CancellationToken cancellationToken) =>
        Notify(() => Task.FromResult(new List<Guid> { teamId }), cancellationToken);

    protected async Task Notify(Func<Task<List<Guid>>> teamIds, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var teamId in (await teamIds()).Distinct())
            {
                await teamHub.Clients.Group(teamId.ToString()).SendAsync(ApplicationsChanged, teamId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not tell teams that their applications changed");
        }
    }

    /// <summary>The teams that show <paramref name="applicationId"/>.</summary>
    protected async Task<List<Guid>> TeamsShowingApplication(Guid applicationId, CancellationToken cancellationToken) =>
        await dbContext.ApplicationInstances
            .Where(x => x.ApplicationId == applicationId)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>The teams in <paramref name="viewId"/> that show an application.</summary>
    protected async Task<List<Guid>> TeamsShowingApplicationsInView(Guid viewId, CancellationToken cancellationToken) =>
        await dbContext.ApplicationInstances
            .Where(x => x.Team.ViewId == viewId)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>The teams that show an application built from <paramref name="templateId"/>.</summary>
    protected async Task<List<Guid>> TeamsShowingTemplate(Guid templateId, CancellationToken cancellationToken) =>
        await dbContext.ApplicationInstances
            .Where(x => x.Application.ApplicationTemplateId == templateId)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);
}

public class ApplicationInstanceCreatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityCreated<ApplicationInstanceEntity>>
{
    public async Task Handle(EntityCreated<ApplicationInstanceEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(notification.Entity.TeamId, cancellationToken);
    }
}

public class ApplicationInstanceUpdatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityUpdated<ApplicationInstanceEntity>>
{
    public async Task Handle(EntityUpdated<ApplicationInstanceEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(notification.Entity.TeamId, cancellationToken);
    }
}

public class ApplicationInstanceDeletedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityDeleted<ApplicationInstanceEntity>>
{
    public async Task Handle(EntityDeleted<ApplicationInstanceEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(notification.Entity.TeamId, cancellationToken);
    }
}

/// <summary>
/// An application's name, URL, icon and flags show on every team it is assigned to. A new application is
/// on no team yet, and a deleted one takes its instances with it, which raise their own events.
/// </summary>
public class ApplicationUpdatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityUpdated<ApplicationEntity>>
{
    public async Task Handle(EntityUpdated<ApplicationEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(() => TeamsShowingApplication(notification.Entity.Id, cancellationToken), cancellationToken);
    }
}

/// <summary>
/// An application leaves unset any property it takes from its template, so a template edit can change what
/// every team showing one of its applications sees.
/// </summary>
public class ApplicationTemplateUpdatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityUpdated<ApplicationTemplateEntity>>
{
    public async Task Handle(EntityUpdated<ApplicationTemplateEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(() => TeamsShowingTemplate(notification.Entity.Id, cancellationToken), cancellationToken);
    }
}

/// <summary>An application's name and url can show its team's name as <c>{teamName}</c>.</summary>
public class TeamRenamedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityUpdated<TeamEntity>>
{
    public async Task Handle(EntityUpdated<TeamEntity> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Contains(nameof(TeamEntity.Name)))
        {
            await Notify(notification.Entity.Id, cancellationToken);
        }
    }
}

/// <summary>An application's name and url can show its view's name as <c>{viewName}</c>.</summary>
public class ViewRenamedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db, ILogger<ApplicationsBaseTeamHubHandler> logger)
    : ApplicationsBaseTeamHubHandler(teamHub, db, logger),
    INotificationHandler<EntityUpdated<ViewEntity>>
{
    public async Task Handle(EntityUpdated<ViewEntity> notification, CancellationToken cancellationToken)
    {
        if (notification.ModifiedProperties.Contains(nameof(ViewEntity.Name)))
        {
            await Notify(() => TeamsShowingApplicationsInView(notification.Entity.Id, cancellationToken), cancellationToken);
        }
    }
}
