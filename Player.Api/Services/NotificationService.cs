// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Player.Api.Services
{
    public interface INotificationService
    {
        Task<IEnumerable<ViewModels.Notification>> GetAsync(CancellationToken ct);
        Task<IEnumerable<ViewModels.Notification>> GetByViewAsync(Guid viewId, CancellationToken ct);
        Task<IEnumerable<ViewModels.Notification>> GetAllViewNotificationsAsync(Guid viewId, CancellationToken ct);
        Task<IEnumerable<ViewModels.Notification>> GetByTeamAsync(Guid teamId, CancellationToken ct);
        Task<IEnumerable<ViewModels.Notification>> GetByUserAsync(Guid viewId, Guid userId, CancellationToken ct);
        Task<ViewModels.Notification> JoinView(Guid id, CancellationToken ct);
        Task<ViewModels.Notification> PostToView(Guid id, ViewModels.Notification incomingData, CancellationToken ct);
        Task<ViewModels.Notification> JoinTeam(Guid teamId, CancellationToken ct);
        Task<ViewModels.Notification> PostToTeam(Guid teamId, ViewModels.Notification incomingData, CancellationToken ct);
        Task<ViewModels.Notification> JoinUser(Guid viewId, Guid userId, CancellationToken ct);
        Task<ViewModels.Notification> PostToUser(Guid viewId, Guid userId, ViewModels.Notification incomingData, CancellationToken ct);
        Task<bool> DeleteAsync(int key, CancellationToken ct);
        Task<bool> DeleteViewNotificationsAsync(Guid viewId, CancellationToken ct);
    }

    public class NotificationService : INotificationService
    {
        private readonly PlayerContext _context;
        private readonly IPlayerAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private IUserClaimsService _claimsService;
        private IConfiguration _configuration;


        public NotificationService(PlayerContext context, IPrincipal user, IPlayerAuthorizationService authorizationService, IMapper mapper, IUserClaimsService claimsService, IConfiguration configuration)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _claimsService = claimsService;
            _configuration = configuration;
        }

        public async Task<IEnumerable<ViewModels.Notification>> GetAsync(CancellationToken ct)
        {
            if (!await _authorizationService.Authorize([SystemPermission.ViewViews], ct))
                throw new ForbiddenException();

            var items = await _context.Notifications.ToListAsync(ct);
            // Databases do not preserve DateTimeKind, so we need to add UTC kind
            items.ForEach(item => item.BroadcastTime = DateTime.SpecifyKind(item.BroadcastTime, DateTimeKind.Utc));
            return _mapper.Map<IEnumerable<ViewModels.Notification>>(items);
        }

        public async Task<IEnumerable<ViewModels.Notification>> GetByViewAsync(Guid viewId, CancellationToken ct)
        {
            if (!_authorizationService.GetAuthorizedViewIds().Contains(viewId))
                throw new ForbiddenException();
            // get all notifications for the selected view, not including the System notifications
            var items = await _context.Notifications.Where(x => x.ToId == viewId && x.ToType == NotificationType.View && x.Priority != NotificationPriority.System).OrderByDescending(y => y.BroadcastTime).ToListAsync();
            // Databases do not preserve DateTimeKind, so we need to add UTC kind
            items.ForEach(item => item.BroadcastTime = DateTime.SpecifyKind(item.BroadcastTime, DateTimeKind.Utc));
            return _mapper.Map<IEnumerable<ViewModels.Notification>>(items);
        }

        public async Task<IEnumerable<ViewModels.Notification>> GetAllViewNotificationsAsync(Guid viewId, CancellationToken ct)
        {
            // get all notifications for the selected view
            var items = await _context.Notifications.Where(n => n.ToId == viewId || n.ViewId == viewId).OrderByDescending(y => y.BroadcastTime).ToListAsync();
            // Databases do not preserve DateTimeKind, so we need to add UTC kind
            items.ForEach(item => item.BroadcastTime = DateTime.SpecifyKind(item.BroadcastTime, DateTimeKind.Utc));
            return _mapper.Map<IEnumerable<ViewModels.Notification>>(items);
        }

        public async Task<IEnumerable<ViewModels.Notification>> GetByTeamAsync(Guid teamId, CancellationToken ct)
        {
            if (!await _authorizationService.Authorize<TeamEntity>(teamId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [TeamPermission.ViewTeam], ct))
                throw new ForbiddenException();
            // get all notifications for the selected team, not including the System notifications
            var items = await _context.Notifications.Where(x => x.ToId == teamId && x.ToType == NotificationType.Team && x.Priority != NotificationPriority.System).OrderByDescending(y => y.BroadcastTime).ToListAsync();
            // Databases do not preserve DateTimeKind, so we need to add UTC kind
            items.ForEach(item => item.BroadcastTime = DateTime.SpecifyKind(item.BroadcastTime, DateTimeKind.Utc));
            return _mapper.Map<IEnumerable<ViewModels.Notification>>(items);
        }

        public async Task<IEnumerable<ViewModels.Notification>> GetByUserAsync(Guid viewId, Guid userId, CancellationToken ct)
        {
            if (!_authorizationService.GetAuthorizedViewIds().Contains(viewId) && _user.GetId() != userId)
                throw new ForbiddenException();
            // get all notifications for the selected view-user, not including the System notifications
            var items = await _context.Notifications.Where(x => x.ToId == userId && x.ToType == NotificationType.User && x.Priority != NotificationPriority.System).OrderByDescending(y => y.BroadcastTime).ToListAsync();
            // Databases do not preserve DateTimeKind, so we need to add UTC kind
            items.ForEach(item => item.BroadcastTime = DateTime.SpecifyKind(item.BroadcastTime, DateTimeKind.Utc));
            return _mapper.Map<IEnumerable<ViewModels.Notification>>(items);
        }

        /// <summary>
        /// Allows a client to join view notifications, IF:
        ///     1. they are a member of this view
        /// </summary>
        /// <param name="viewId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ViewModels.Notification> JoinView(Guid viewId, CancellationToken cancellationToken)
        {
            var wasSuccess = true;
            var canPost = false;
            var messageTime = DateTime.Now.ToUniversalTime();
            var notificationEntity = new NotificationEntity { };
            notificationEntity.Subject = "Join View";
            notificationEntity.ToId = viewId;
            notificationEntity.ToType = NotificationType.View;
            notificationEntity.BroadcastTime = messageTime;
            notificationEntity.FromId = _user.GetId();
            notificationEntity.FromType = NotificationType.User;
            notificationEntity.FromName = _user.Claims.Single(x => x.Type == "name").Value;
            notificationEntity.ToName = _context.Views.Find(viewId).Name;
            notificationEntity.Priority = NotificationPriority.System;
            if (await _authorizationService.Authorize<ViewEntity>(viewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken))
            {
                notificationEntity.Text = String.Format("Successfully joined {0} notifications.", notificationEntity.ToName);
                canPost = true;
            }
            else if (_authorizationService.GetAuthorizedViewIds().Contains(viewId))
            {
                notificationEntity.Text = String.Format("Successfully joined {0} notifications.", notificationEntity.ToName);
            }
            else
            {
                notificationEntity.Text = String.Format("Failed to join {0} notifications.", notificationEntity.ToName);
                wasSuccess = false;
            }
            var returnNotification = _mapper.Map<ViewModels.Notification>(notificationEntity);
            returnNotification.WasSuccess = wasSuccess;
            returnNotification.CanPost = canPost;
            returnNotification.IconUrl = GetIconUrl(notificationEntity.Priority, notificationEntity.FromName);
            return returnNotification;
        }

        /// <summary>
        /// Allows a client to broadcast a notification to all members of the view who are currently joined
        /// </summary>
        /// <param name="viewId"></param>
        /// <param name="incomingData"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ViewModels.Notification> PostToView(Guid viewId, ViewModels.Notification incomingData, CancellationToken cancellationToken)
        {
            var wasSuccess = true;
            var canPost = true;
            var messageTime = DateTime.Now.ToUniversalTime();
            var notificationEntity = _mapper.Map<NotificationEntity>(incomingData);
            notificationEntity.ToId = viewId;
            notificationEntity.ToType = NotificationType.View;
            notificationEntity.BroadcastTime = messageTime;
            notificationEntity.FromId = _user.GetId();
            notificationEntity.FromType = NotificationType.User;
            notificationEntity.FromName = _user.Claims.Single(x => x.Type == "name").Value;
            notificationEntity.ToName = _context.Views.Find(viewId).Name;
            _context.Notifications.Add(notificationEntity);
            await _context.SaveChangesAsync(cancellationToken);

            var returnNotification = _mapper.Map<ViewModels.Notification>(notificationEntity);
            returnNotification.WasSuccess = wasSuccess;
            returnNotification.CanPost = canPost;
            returnNotification.IconUrl = GetIconUrl(notificationEntity.Priority, notificationEntity.FromName);
            return returnNotification;
        }

        /// <summary>
        /// Allows a client to join team notifications, IF:
        ///     1. they are a member of this team OR
        ///     2. they are an view administrator
        /// </summary>
        /// <param name="teamId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ViewModels.Notification> JoinTeam(Guid teamId, CancellationToken cancellationToken)
        {
            var wasSuccess = true;
            var canPost = false;
            var messageTime = DateTime.Now.ToUniversalTime();
            var requestedTeam = _context.Teams.Find(teamId);
            var notificationEntity = new NotificationEntity { };
            notificationEntity.Subject = "Join Team";
            notificationEntity.ToId = teamId;
            notificationEntity.ToType = NotificationType.View;
            notificationEntity.BroadcastTime = messageTime;
            notificationEntity.FromId = _user.GetId();
            notificationEntity.FromType = NotificationType.User;
            notificationEntity.FromName = _user.Claims.Single(x => x.Type == "name").Value;
            notificationEntity.ToName = requestedTeam.Name;
            notificationEntity.Priority = NotificationPriority.System;
            if (await _authorizationService.Authorize<ViewEntity>(requestedTeam.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken))
            {
                notificationEntity.Text = String.Format("Successfully joined {0} notifications.", notificationEntity.ToName);
                canPost = true;
            }
            else if (await _authorizationService.Authorize<TeamEntity>(teamId, [], [], [TeamPermission.ViewTeam], cancellationToken))
            {
                notificationEntity.Text = String.Format("Successfully joined {0} notifications.", notificationEntity.ToName);
            }
            else
            {
                notificationEntity.Text = String.Format("Failed to join {0} notifications.", notificationEntity.ToName);
                wasSuccess = false;
            }
            var returnNotification = _mapper.Map<ViewModels.Notification>(notificationEntity);
            returnNotification.WasSuccess = wasSuccess;
            returnNotification.CanPost = canPost;
            returnNotification.IconUrl = GetIconUrl(notificationEntity.Priority, notificationEntity.FromName);
            return returnNotification;
        }

        /// <summary>
        /// Allows a client to broadcast a notification to all members of the team who are currently joined
        /// </summary>
        /// <param name="teamId"></param>
        /// <param name="incomingData"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<ViewModels.Notification> PostToTeam(Guid teamId, ViewModels.Notification incomingData, CancellationToken ct)
        {
            var wasSuccess = true;
            var canPost = true;
            var messageTime = DateTime.Now.ToUniversalTime();
            var requestedTeam = _context.Teams.Find(teamId);
            NotificationEntity notificationEntity = _mapper.Map<NotificationEntity>(incomingData);
            notificationEntity.ToId = teamId;
            notificationEntity.ToType = NotificationType.Team;
            notificationEntity.BroadcastTime = messageTime;
            notificationEntity.FromId = _user.GetId();
            notificationEntity.FromType = NotificationType.User;
            notificationEntity.FromName = _user.Claims.Single(x => x.Type == "name").Value;
            notificationEntity.ToName = _context.Teams.Find(teamId).Name;
            _context.Notifications.Add(notificationEntity);
            await _context.SaveChangesAsync(ct);

            var returnNotification = _mapper.Map<ViewModels.Notification>(notificationEntity);
            returnNotification.WasSuccess = wasSuccess;
            returnNotification.CanPost = canPost;
            returnNotification.IconUrl = GetIconUrl(notificationEntity.Priority, notificationEntity.FromName);
            return returnNotification;
        }

        /// <summary>
        /// Allows a client to join the user notifications, IF:
        ///     1. they are a member of the indicated view and they are the indicated user OR
        ///     2. they are an view administrator
        /// </summary>
        /// <param name="viewId"></param>
        /// <param name="userId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<ViewModels.Notification> JoinUser(Guid viewId, Guid userId, CancellationToken ct)
        {
            var wasSuccess = true;
            var canPost = false;
            var messageTime = DateTime.Now.ToUniversalTime();
            var notificationEntity = new NotificationEntity { };
            notificationEntity.Subject = "Join Team";
            notificationEntity.ViewId = viewId;
            notificationEntity.ToId = userId;
            notificationEntity.ToType = NotificationType.View;
            notificationEntity.BroadcastTime = messageTime;
            notificationEntity.FromId = _user.GetId();
            notificationEntity.FromType = NotificationType.User;
            notificationEntity.FromName = _user.Claims.Single(x => x.Type == "name").Value;
            notificationEntity.ToName = notificationEntity.FromName;
            notificationEntity.Priority = NotificationPriority.System;

            if (await _authorizationService.Authorize<ViewEntity>(viewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], ct))
            {
                notificationEntity.Text = String.Format("Successfully joined {0} notifications.", notificationEntity.ToName);
                canPost = true;
            }
            else
            {
                var isViewMember = _authorizationService.GetAuthorizedViewIds().Contains(viewId);
                var isSameUser = _user.GetId() == userId;
                if (isViewMember && isSameUser)
                {
                    notificationEntity.Text = String.Format("Successfully joined {0} notifications.", notificationEntity.ToName);
                }
                else
                {
                    notificationEntity.Text = String.Format("Failed to join {0} notifications.", notificationEntity.ToName);
                    wasSuccess = false;
                }
            }
            var returnNotification = _mapper.Map<ViewModels.Notification>(notificationEntity);
            returnNotification.WasSuccess = wasSuccess;
            returnNotification.CanPost = canPost;
            returnNotification.IconUrl = GetIconUrl(notificationEntity.Priority, notificationEntity.FromName);
            return returnNotification;
        }

        /// <summary>
        /// Allows a client to broadcast a notification to the joined view user, IF:
        ///     1. they are an view administrator
        /// </summary>
        /// <param name="viewId"></param>
        /// <param name="userId"></param>
        /// <param name="incomingData"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ViewModels.Notification> PostToUser(Guid viewId, Guid userId, ViewModels.Notification incomingData, CancellationToken cancellationToken)
        {
            var wasSuccess = true;
            var canPost = true;
            var messageTime = DateTime.Now.ToUniversalTime();

            var notificationEntity = _mapper.Map<NotificationEntity>(incomingData);
            notificationEntity.ViewId = viewId;
            notificationEntity.ToId = userId;
            notificationEntity.ToType = NotificationType.User;
            notificationEntity.BroadcastTime = messageTime;
            notificationEntity.FromId = _user.GetId();
            notificationEntity.FromType = NotificationType.User;
            notificationEntity.FromName = _user.Claims.Single(x => x.Type == "name").Value;
            notificationEntity.ToName = _context.Users.Single(x => x.Id == userId).Name;
            _context.Notifications.Add(notificationEntity);
            await _context.SaveChangesAsync(cancellationToken);

            var returnNotification = _mapper.Map<ViewModels.Notification>(notificationEntity);
            returnNotification.WasSuccess = wasSuccess;
            returnNotification.CanPost = canPost;
            returnNotification.IconUrl = GetIconUrl(notificationEntity.Priority, notificationEntity.FromName);
            return returnNotification;
        }

        public async Task<bool> DeleteAsync(int key, CancellationToken ct)
        {
            var notificationToDelete = await _context.Notifications.SingleOrDefaultAsync(n => n.Key == key, ct);

            if (notificationToDelete == null)
                throw new EntityNotFoundException<ViewModels.Notification>();

            _context.Notifications.Remove(notificationToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        public async Task<bool> DeleteViewNotificationsAsync(Guid viewId, CancellationToken ct)
        {
            var notificationsToDelete = await _context.Notifications.Where(n => n.ToId == viewId || n.ViewId == viewId).ToListAsync();

            if (notificationsToDelete == null || !notificationsToDelete.Any())
                return false;

            _context.Notifications.RemoveRange(notificationsToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }


        private string GetIconUrl(NotificationPriority notificationPriority, string userName)
        {
            var iconUrl = "";

            if (_user.HasClaim(c => c.Type == "client_logo"))
            {
                iconUrl = _user.Claims.Where(x => x.Type == "client_logo").FirstOrDefault().Value;
            }
            else if (notificationPriority == NotificationPriority.System)
            {
                iconUrl = _configuration["Notifications:SystemIconUrl"];
            }
            else
            {
                iconUrl = _configuration["Notifications:UserIconUrl"];
            }
            return iconUrl;
        }
    }
}
