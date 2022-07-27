// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Player.Api.Services
{
    public interface IViewService
    {
        Task<IEnumerable<ViewModels.View>> GetAsync(CancellationToken ct);
        Task<ViewModels.View> GetAsync(Guid id, CancellationToken ct);
        Task<IEnumerable<ViewModels.View>> GetByUserIdAsync(Guid userId, CancellationToken ct);
        Task<ViewModels.View> CreateAsync(ViewModels.ViewForm view, CancellationToken ct);
        Task<View> CloneAsync(Guid id, ViewCloneOverride viewCloneOverride, CancellationToken ct);
        Task<ViewModels.View> UpdateAsync(Guid id, ViewModels.View view, CancellationToken ct);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    }

    public class ViewService : IViewService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private IUserClaimsService _claimsService;
        private readonly IFileService _fileService;

        public ViewService(PlayerContext context, IAuthorizationService authorizationService, IPrincipal user, IMapper mapper, IUserClaimsService claimsService, IFileService fileService)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _claimsService = claimsService;
            _fileService = fileService;
        }

        public async Task<IEnumerable<ViewModels.View>> GetAsync(CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var items = await _context.Views
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<View>>(items);
        }

        public async Task<ViewModels.View> GetAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded &&
                !(await _authorizationService.AuthorizeAsync(_user, null, new ViewMemberRequirement(id))).Succeeded)
                throw new ForbiddenException();

            var item = await _context.Views
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            return _mapper.Map<View>(item);
        }

        public async Task<IEnumerable<ViewModels.View>> GetByUserIdAsync(Guid userId, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserRequirement(userId))).Succeeded)
                throw new ForbiddenException();

            var user = await _context.Users
                .Include(u => u.ViewMemberships)
                    .ThenInclude(em => em.View)
                .Where(u => u.Id == userId)
                .SingleOrDefaultAsync(ct);

            if (user == null)
                throw new EntityNotFoundException<User>();

            var views = user.ViewMemberships.Select(x => x.View);

            return _mapper.Map<IEnumerable<ViewModels.View>>(views);
        }

        public async Task<ViewModels.View> CreateAsync(ViewModels.ViewForm view, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewCreationRequirement())).Succeeded)
                throw new ForbiddenException();

            var viewEntity = _mapper.Map<ViewEntity>(view);

            var viewAdminPermission = await _context.Permissions
                .Where(p => p.Key == PlayerClaimTypes.ViewAdmin.ToString())
                .FirstOrDefaultAsync(ct);

            if (viewAdminPermission == null)
                throw new EntityNotFoundException<Permission>($"{PlayerClaimTypes.ViewAdmin.ToString()} Permission not found.");

            var userId = _user.GetId();

            TeamEntity teamEntity = null;
            ViewMembershipEntity viewMembershipEntity = null;
            // Create an Admin team with the caller as a member
            if (view.CreateAdminTeam)
            {
                teamEntity = new TeamEntity() { Name = "Admin" };
                teamEntity.Permissions.Add(new TeamPermissionEntity() { Permission = viewAdminPermission });

                viewMembershipEntity = new ViewMembershipEntity { View = viewEntity, UserId = userId };
                viewEntity.Teams.Add(teamEntity);
                viewEntity.Memberships.Add(viewMembershipEntity);

            }

            _context.Views.Add(viewEntity);
            await _context.SaveChangesAsync(ct);

            if (view.CreateAdminTeam)
            {
                var teamMembershipEntity = new TeamMembershipEntity { Team = teamEntity, UserId = userId, ViewMembership = viewMembershipEntity };
                viewMembershipEntity.PrimaryTeamMembership = teamMembershipEntity;
                _context.TeamMemberships.Add(teamMembershipEntity);
                _context.ViewMemberships.Update(viewMembershipEntity);
                await _context.SaveChangesAsync(ct);
            }

            await _context.SaveChangesAsync(ct);

            return await GetAsync(viewEntity.Id, ct);
        }

        public async Task<View> CloneAsync(Guid idToBeCloned, ViewCloneOverride viewCloneOverride, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewCreationRequirement())).Succeeded)
                throw new ForbiddenException();

            var view = await _context.Views
                .Include(o => o.Teams)
                    .ThenInclude(o => o.Applications)
                .Include(o => o.Teams)
                    .ThenInclude(o => o.Permissions)
                .Include(o => o.Applications)
                    .ThenInclude(o => o.Template)
                .Include(o => o.Files)
                .SingleOrDefaultAsync(o => o.Id == idToBeCloned, ct);

            var newView = view.Clone();
            newView.Name = $"Clone of {newView.Name}";
            newView.Status = ViewStatus.Active;
            if (viewCloneOverride != null)
            {
                newView.Name = string.IsNullOrWhiteSpace(viewCloneOverride.Name) ? newView.Name : viewCloneOverride.Name;
                newView.Description = string.IsNullOrWhiteSpace(viewCloneOverride.Description) ? newView.Description : viewCloneOverride.Description;
            }

            //copy view applications
            foreach (var application in view.Applications)
            {
                var newApplication = application.Clone();
                newView.Applications.Add(newApplication);
            }

            //copy teams
            foreach (var team in view.Teams)
            {
                var newTeam = team.Clone();

                //copy team applications
                foreach (var applicationInstance in team.Applications)
                {
                    var newApplicationInstance = applicationInstance.Clone();

                    var application = view.Applications.FirstOrDefault(o => o.Id == applicationInstance.ApplicationId);
                    var newApplication = newView.Applications.FirstOrDefault(o => application != null && o.GetName() == application.GetName());

                    newApplicationInstance.Application = newApplication;

                    newTeam.Applications.Add(newApplicationInstance);
                }

                //copy team permissions
                foreach (var permission in team.Permissions)
                {
                    var newPermission = new TeamPermissionEntity(newTeam.Id, permission.PermissionId);
                    newTeam.Permissions.Add(newPermission);
                }

                newView.Teams.Add(newTeam);
            }

            // Copy files - note that the files themselves are not being copied, just the pointers
            foreach (var file in view.Files)
            {
                var cloned = file.Clone();
                cloned.View = newView;
                newView.Files.Add(cloned);
            }

            _context.Add(newView);
            await _context.SaveChangesAsync(ct);

            // SaveChanges is called twice because we need the new IDs for each time.
            // Should figure out a better way to do it.
            foreach (var file in newView.Files)
            {
                List<Guid> newTeamIds = new List<Guid>();
                foreach (var team in file.TeamIds)
                {
                    var teamName = view.Teams.FirstOrDefault(t => t.Id == team).Name;
                    var newId = file.View.Teams.FirstOrDefault(t => t.Name == teamName).Id;
                    newTeamIds.Add(newId);
                }
                file.TeamIds = newTeamIds;
            }
            await _context.SaveChangesAsync(ct);
            return _mapper.Map<View>(newView);
        }

        public async Task<ViewModels.View> UpdateAsync(Guid id, ViewModels.View view, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(id))).Succeeded)
                throw new ForbiddenException();

            var viewToUpdate = await _context.Views.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (viewToUpdate == null)
                throw new EntityNotFoundException<View>();

            _mapper.Map(view, viewToUpdate);

            _context.Views.Update(viewToUpdate);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map(viewToUpdate, view);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(id))).Succeeded)
                throw new ForbiddenException();

            var viewToDelete = await _context.Views.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (viewToDelete == null)
                throw new EntityNotFoundException<View>();

            // Delete files within this view
            var files = await _fileService.GetByViewAsync(id, ct);
            foreach (var fp in files)
            {
                await _fileService.DeleteAsync(fp.id, ct);
            }

            _context.Views.Remove(viewToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }
    }

    public class ViewCloneOverride
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

}
