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
public class ApplicationsBaseTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext dbContext)
{
    public const string ApplicationsChanged = "ApplicationsChanged";

    protected async Task Notify(IEnumerable<Guid> teamIds, CancellationToken cancellationToken)
    {
        foreach (var teamId in teamIds.Distinct())
        {
            await teamHub.Clients.Group(teamId.ToString()).SendAsync(ApplicationsChanged, teamId, cancellationToken);
        }
    }

    /// <summary>The teams that show <paramref name="applicationId"/>.</summary>
    protected async Task<List<Guid>> TeamsShowingApplication(Guid applicationId, CancellationToken cancellationToken) =>
        await dbContext.ApplicationInstances
            .Where(x => x.ApplicationId == applicationId)
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

public class ApplicationInstanceCreatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db) : ApplicationsBaseTeamHubHandler(teamHub, db),
    INotificationHandler<EntityCreated<ApplicationInstanceEntity>>
{
    public async Task Handle(EntityCreated<ApplicationInstanceEntity> notification, CancellationToken cancellationToken)
    {
        await Notify([notification.Entity.TeamId], cancellationToken);
    }
}

public class ApplicationInstanceUpdatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db) : ApplicationsBaseTeamHubHandler(teamHub, db),
    INotificationHandler<EntityUpdated<ApplicationInstanceEntity>>
{
    public async Task Handle(EntityUpdated<ApplicationInstanceEntity> notification, CancellationToken cancellationToken)
    {
        await Notify([notification.Entity.TeamId], cancellationToken);
    }
}

public class ApplicationInstanceDeletedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db) : ApplicationsBaseTeamHubHandler(teamHub, db),
    INotificationHandler<EntityDeleted<ApplicationInstanceEntity>>
{
    public async Task Handle(EntityDeleted<ApplicationInstanceEntity> notification, CancellationToken cancellationToken)
    {
        await Notify([notification.Entity.TeamId], cancellationToken);
    }
}

/// <summary>
/// An application's name, URL, icon and flags show on every team it is assigned to. A new application is
/// on no team yet, and a deleted one takes its instances with it, which raise their own events.
/// </summary>
public class ApplicationUpdatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db) : ApplicationsBaseTeamHubHandler(teamHub, db),
    INotificationHandler<EntityUpdated<ApplicationEntity>>
{
    public async Task Handle(EntityUpdated<ApplicationEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(await TeamsShowingApplication(notification.Entity.Id, cancellationToken), cancellationToken);
    }
}

/// <summary>
/// An application leaves unset any property it takes from its template, so a template edit can change what
/// every team showing one of its applications sees.
/// </summary>
public class ApplicationTemplateUpdatedTeamHubHandler(IHubContext<TeamHub> teamHub, PlayerContext db) : ApplicationsBaseTeamHubHandler(teamHub, db),
    INotificationHandler<EntityUpdated<ApplicationTemplateEntity>>
{
    public async Task Handle(EntityUpdated<ApplicationTemplateEntity> notification, CancellationToken cancellationToken)
    {
        await Notify(await TeamsShowingTemplate(notification.Entity.Id, cancellationToken), cancellationToken);
    }
}
