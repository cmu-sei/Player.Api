// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Player.Api.Services
{
    public interface IPresenceService
    {
        Task<Guid?> AddConnectionToView(Guid viewId, Guid userId, string connectionId, CancellationToken ct);
        Task<Guid?> RemoveConnectionFromView(Guid viewMembershipId, Guid userId, string connectionId, CancellationToken ct);
        Task<string> GetGroupByViewId(Guid viewId);
        Task<IEnumerable<ViewPresence>> GetPresenceByViewId(Guid viewId);
    }

    public class PresenceService : IPresenceService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly IHubContext<ViewHub> _hub;
        private readonly ConnectionCacheService _connectionCacheService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public PresenceService(
            PlayerContext context,
            IAuthorizationService authorizationService,
            IHubContext<ViewHub> hub,
            IPrincipal user,
            IMapper mapper,
            ConnectionCacheService connectionCacheService)
        {
            _context = context;
            _authorizationService = authorizationService;
            _hub = hub;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _connectionCacheService = connectionCacheService;
        }

        public async Task<Guid?> AddConnectionToView(Guid viewId, Guid userId, string connectionId, CancellationToken ct)
        {
            Guid? returnVal = null;

            var viewMembership = await _context.ViewMemberships
                .Include(x => x.TeamMemberships)
                .Include(x => x.User)
                .Where(x => x.UserId == userId && x.ViewId == viewId)
                .SingleOrDefaultAsync(ct);

            if (viewMembership != null)
            {
                var connectionIds = _connectionCacheService.ViewMembershipConnections.AddOrUpdate(
                    viewMembership.Id,
                    new List<string>(new[] { connectionId }),
                    (id, connectionIds) =>
                    {
                        var viewLock = _connectionCacheService.Locks.GetOrAdd(viewMembership.Id, new object());

                        lock (viewLock)
                        {
                            if (!connectionIds.Contains(connectionId))
                            {
                                connectionIds.Add(connectionId);
                            }
                        }

                        return connectionIds;
                    });


                var groupList = new List<string> { GetGroup(viewId) };

                foreach (var teamId in viewMembership.TeamMemberships.Select(x => x.TeamId))
                {
                    groupList.Add(GetGroup(teamId));
                }

                await _hub.Clients.Groups(groupList).SendAsync("PresenceUpdate", new ViewPresence
                {
                    Id = viewMembership.Id,
                    Online = connectionIds.Any(),
                    UserId = userId,
                    UserName = viewMembership.User.Name,
                    ViewId = viewId
                });

                returnVal = viewMembership.Id;
            }

            return returnVal;
        }

        public async Task<Guid?> RemoveConnectionFromView(Guid viewMembershipId, Guid userId, string connectionId, CancellationToken ct)
        {
            Guid? returnVal = null;

            var viewMembership = await _context.ViewMemberships
                .Include(x => x.TeamMemberships)
                .Include(x => x.User)
                .Where(x => x.Id == viewMembershipId)
                .SingleOrDefaultAsync(ct);

            if (viewMembership != null)
            {
                var viewLock = _connectionCacheService.Locks.GetOrAdd(viewMembership.Id, new object());
                List<string> connectionList;

                if (_connectionCacheService.ViewMembershipConnections.TryGetValue(viewMembershipId, out connectionList))
                {
                    bool online = false;

                    var groupList = new List<string> { GetGroup(viewMembership.ViewId) };

                    foreach (var teamId in viewMembership.TeamMemberships.Select(x => x.TeamId))
                    {
                        groupList.Add(GetGroup(teamId));
                    }

                    lock (viewLock)
                    {
                        connectionList.Remove(connectionId);
                        online = connectionList.Any();
                    }

                    await _hub.Clients.Groups(groupList).SendAsync("PresenceUpdate", new ViewPresence
                    {
                        Id = viewMembership.Id,
                        Online = online,
                        UserId = userId,
                        UserName = viewMembership.User.Name,
                        ViewId = viewMembership.ViewId
                    });

                    returnVal = viewMembership.Id;
                }
            }

            return returnVal;
        }

        public async Task<IEnumerable<ViewPresence>> GetPresenceByViewId(Guid viewId)
        {
            ViewMembershipEntity[] viewMembershipList;

            if ((await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(viewId))).Succeeded)
            {
                viewMembershipList = await _context.ViewMemberships
                    .Include(x => x.User)
                    .Include(x => x.TeamMemberships)
                    .Where(x => x.ViewId == viewId)
                    .ToArrayAsync();
            }
            else
            {
                var team = await _context.ViewMemberships
                    .Where(x => x.ViewId == viewId && x.UserId == _user.GetId())
                    .Select(x => x.PrimaryTeamMembership.Team)
                    .FirstOrDefaultAsync();

                viewMembershipList = await _context.TeamMemberships
                    .Include(x => x.ViewMembership)
                        .ThenInclude(x => x.User)
                    .Include(x => x.ViewMembership)
                        .ThenInclude(x => x.TeamMemberships)
                    .Where(x => x.TeamId == team.Id)
                    .Select(x => x.ViewMembership)
                    .ToArrayAsync();
            }

            var viewPresenceList = new List<ViewPresence>();

            foreach (var viewMembership in viewMembershipList)
            {
                List<string> connectionList;

                var viewPresence = new ViewPresence
                {
                    Id = viewMembership.Id,
                    Online = false,
                    UserId = viewMembership.UserId,
                    UserName = viewMembership.User.Name,
                    TeamIds = viewMembership.TeamMemberships.Select(x => x.TeamId).ToArray(),
                    ViewId = viewMembership.ViewId
                };

                if (_connectionCacheService.ViewMembershipConnections.TryGetValue(viewMembership.Id, out connectionList))
                {
                    var viewMembershipLock = _connectionCacheService.Locks.GetOrAdd(viewMembership.Id, new object());

                    lock (viewMembershipLock)
                    {
                        viewPresence.Online = connectionList.Any();
                    }
                }

                viewPresenceList.Add(viewPresence);
            }

            return viewPresenceList.ToArray();
        }

        public async Task<string> GetGroupByViewId(Guid viewId)
        {
            if ((await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(viewId))).Succeeded)
            {
                return GetGroup(viewId);
            }
            else
            {
                var teamMembership = await _context.ViewMemberships
                    .Where(x => x.UserId == _user.GetId() && x.ViewId == viewId)
                    .Select(x => x.PrimaryTeamMembership)
                    .FirstOrDefaultAsync();

                if (teamMembership != null)
                {
                    return GetGroup(teamMembership.TeamId);
                }
                else
                {
                    return null;
                }
            }
        }

        public IEnumerable<string> GetGroups(Guid[] groupIds)
        {
            var groupList = new List<string>();

            foreach (var groupId in groupIds)
            {
                groupList.Add(GetGroup(groupId));
            }

            return groupList;
        }

        private string GetGroup(Guid groupId)
        {
            return $"Presence-{groupId}";
        }
    }
}
