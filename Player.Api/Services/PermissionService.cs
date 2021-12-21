// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Extensions;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Services
{
    public interface IPermissionService
    {
        Task<IEnumerable<Permission>> GetAsync();
        Task<Permission> GetAsync(Guid id);
        Task<IEnumerable<Permission>> GetByViewIdForUserAsync(Guid viewId, Guid userId);
        Task<IEnumerable<Permission>> GetByTeamIdForUserAsync(Guid teamId, Guid userId);
        Task<Permission> CreateAsync(PermissionForm form);
        Task<Permission> UpdateAsync(Guid id, PermissionForm form);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> AddToRoleAsync(Guid roleId, Guid permissionId);
        Task<bool> RemoveFromRoleAsync(Guid roleId, Guid permissionId);
        Task<bool> AddToTeamAsync(Guid teamId, Guid permissionId);
        Task<bool> RemoveFromTeamAsync(Guid teamId, Guid permissionId);
        Task<bool> AddToUserAsync(Guid userId, Guid permissionId);
        Task<bool> RemoveFromUserAsync(Guid userId, Guid permissionId);
    }

    public class PermissionService : IPermissionService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public PermissionService(PlayerContext context,
                                IAuthorizationService authorizationService,
                                IPrincipal user,
                                IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
        }

        public async Task<IEnumerable<Permission>> GetAsync()
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement())).Succeeded)
                throw new ForbiddenException();

            var items = await _context.Permissions
                .ProjectTo<ViewModels.Permission>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return items;
        }

        public async Task<Permission> GetAsync(Guid id)
        {
            var item = await _context.Permissions
                .ProjectTo<Permission>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id);

            return item;
        }

        public async Task<Permission> CreateAsync(PermissionForm form)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var permissionEntity = _mapper.Map<PermissionEntity>(form);

            _context.Permissions.Add(permissionEntity);
            await _context.SaveChangesAsync();

            return _mapper.Map<Permission>(permissionEntity);
        }

        public async Task<Permission> UpdateAsync(Guid id, PermissionForm form)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var permissionToUpdate = await _context.Permissions.SingleOrDefaultAsync(v => v.Id == id);

            if (permissionToUpdate == null)
                throw new EntityNotFoundException<Permission>();

            if (permissionToUpdate.ReadOnly)
                throw new ForbiddenException("Cannot update a Read-Only Permission");

            _mapper.Map(form, permissionToUpdate);

            _context.Permissions.Update(permissionToUpdate);
            await _context.SaveChangesAsync();

            return _mapper.Map<Permission>(permissionToUpdate);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var permissionToDelete = await _context.Permissions.SingleOrDefaultAsync(t => t.Id == id);

            if (permissionToDelete == null)
                throw new EntityNotFoundException<Permission>();

            if (permissionToDelete.ReadOnly)
                throw new ForbiddenException("Cannot delete a Read-Only Permission");

            _context.Permissions.Remove(permissionToDelete);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<IEnumerable<Permission>> GetByViewIdForUserAsync(Guid viewId, Guid userId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(viewId, userId))).Succeeded)
                throw new ForbiddenException();

            var user = await _context.Users
                .IncludePermissions()
                .SingleOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new EntityNotFoundException<User>();

            return await GetPermissions(viewId, user);
        }

        public async Task<IEnumerable<Permission>> GetByTeamIdForUserAsync(Guid teamId, Guid userId)
        {
            var user = await _context.Users
                .IncludePermissions()
                .Where(u => u.Id == userId)
                .SingleOrDefaultAsync();

            var team = await _context.Teams
                .Where(t => t.Id == teamId)
                .SingleOrDefaultAsync();

            if (user == null)
                throw new EntityNotFoundException<User>();

            if (team == null)
                throw new EntityNotFoundException<Team>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(team.ViewId, userId))).Succeeded)
                throw new ForbiddenException();

            return await GetPermissions(team.ViewId, user);
        }

        private async Task<IEnumerable<Permission>> GetPermissions(Guid viewId, UserEntity user)
        {
            var viewMembershipQuery = _context.ViewMemberships
                .Include(x => x.PrimaryTeamMembership)
                    .ThenInclude(m => m.Role)
                        .ThenInclude(r => r.Permissions)
                            .ThenInclude(p => p.Permission)
                .Include(x => x.PrimaryTeamMembership)
                    .ThenInclude(m => m.Team)
                        .ThenInclude(t => t.Role)
                            .ThenInclude(r => r.Permissions)
                                .ThenInclude(p => p.Permission)
                .Include(x => x.PrimaryTeamMembership)
                    .ThenInclude(m => m.Team.Permissions)
                        .ThenInclude(p => p.Permission)
                .Where(x => x.ViewId == viewId && x.UserId == user.Id);

            ViewMembershipEntity membership = await viewMembershipQuery.FirstOrDefaultAsync();
            List<PermissionEntity> permissions = new List<PermissionEntity>();

            if (membership != null)
            {
                if (membership.PrimaryTeamMembership != null)
                {
                    permissions.Add(new PermissionEntity { Key = "TeamMember", Value = membership.PrimaryTeamMembership.TeamId.ToString() });

                    if (membership.PrimaryTeamMembership.Role != null)
                    {
                        permissions.AddRange(membership.PrimaryTeamMembership.Role.Permissions.Select(x => x.Permission));
                    }

                    if (membership.PrimaryTeamMembership.Team != null)
                    {
                        if (membership.PrimaryTeamMembership.Team.Role != null)
                        {
                            permissions.AddRange(membership.PrimaryTeamMembership.Team.Role.Permissions.Select(x => x.Permission));
                        }

                        if (membership.PrimaryTeamMembership.Team.Permissions.Any())
                        {
                            permissions.AddRange(membership.PrimaryTeamMembership.Team.Permissions.Select(x => x.Permission));
                        }
                    }
                }
            }
            else
            {
                permissions.AddRange(user.Permissions.Select(x => x.Permission));

                if (user.Role != null)
                {
                    foreach (var permission in user.Role.Permissions.Select(x => x.Permission))
                    {
                        if (permission != null && !permissions.Any(x => x.Id == permission.Id))
                        {
                            permissions.Add(permission);
                        }
                    }
                }
            }

            return _mapper.Map<IEnumerable<Permission>>(permissions);
        }

        public async Task<bool> AddToRoleAsync(Guid roleId, Guid permissionId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var role = await _context.Roles
                .Where(r => r.Id == roleId)
                .SingleOrDefaultAsync();

            var permission = await _context.Permissions
                .Where(p => p.Id == permissionId)
                .SingleOrDefaultAsync();

            if (role == null)
                throw new EntityNotFoundException<Role>();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            role.Permissions.Add(new RolePermissionEntity(roleId, permissionId));
            _context.Roles.Update(role);

            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RemoveFromRoleAsync(Guid roleId, Guid permissionId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var role = await _context.Roles
                .Where(r => r.Id == roleId)
                .SingleOrDefaultAsync();

            if (role == null)
                throw new EntityNotFoundException<Role>();

            var permission = await _context.Permissions
                .Where(p => p.Id == permissionId)
                .SingleOrDefaultAsync();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            var rolePermission = await _context.RolePermissions
                .Where(x => x.RoleId == roleId && x.PermissionId == permissionId)
                .SingleOrDefaultAsync();

            if (rolePermission != null)
            {
                _context.RolePermissions.Remove(rolePermission);
                await _context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<bool> AddToTeamAsync(Guid teamId, Guid permissionId)
        {
            var team = await _context.Teams
                .Where(t => t.Id == teamId)
                .FirstOrDefaultAsync();

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var permission = await _context.Permissions
                .Where(p => p.Id == permissionId)
                .FirstOrDefaultAsync();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(team.ViewId))).Succeeded)
                throw new ForbiddenException();

            team.Permissions.Add(new TeamPermissionEntity(teamId, permissionId));
            _context.Teams.Update(team);

            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RemoveFromTeamAsync(Guid teamId, Guid permissionId)
        {
            var team = await _context.Teams
                .Where(t => t.Id == teamId)
                .FirstOrDefaultAsync();

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var permission = await _context.Permissions
                .Where(p => p.Id == permissionId)
                .FirstOrDefaultAsync();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            var teamPermission = await _context.TeamPermissions
                .Where(x => x.TeamId == teamId && x.PermissionId == permissionId)
                .FirstOrDefaultAsync();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(team.ViewId))).Succeeded)
                throw new ForbiddenException();

            if (teamPermission != null)
            {
                _context.TeamPermissions.Remove(teamPermission);
                await _context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<bool> AddToUserAsync(Guid userId, Guid permissionId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var user = await _context.Users
                .Where(t => t.Id == userId)
                .FirstOrDefaultAsync();

            if (user == null)
                throw new EntityNotFoundException<User>();

            var permission = await _context.Permissions
                .Where(p => p.Id == permissionId)
                .FirstOrDefaultAsync();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            user.Permissions.Add(new UserPermissionEntity(userId, permissionId));
            _context.Users.Update(user);

            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RemoveFromUserAsync(Guid userId, Guid permissionId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var user = await _context.Users
                .Where(t => t.Id == userId)
                .FirstOrDefaultAsync();

            if (user == null)
                throw new EntityNotFoundException<User>();

            var permission = await _context.Permissions
                .Where(p => p.Id == permissionId)
                .FirstOrDefaultAsync();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            var userPermission = await _context.UserPermissions
                .Where(x => x.UserId == userId && x.PermissionId == permissionId)
                .FirstOrDefaultAsync();

            if (userPermission != null)
            {
                if (permission.Key == PlayerClaimTypes.SystemAdmin.ToString() && userId == _user.GetId())
                    throw new ForbiddenException($"You cannot remove the {PlayerClaimTypes.SystemAdmin.ToString()} permission from yourself.");

                _context.UserPermissions.Remove(userPermission);
                await _context.SaveChangesAsync();
            }

            return true;
        }
    }
}
