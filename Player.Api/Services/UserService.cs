// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Services
{
    public interface IUserService
    {
        Task<IEnumerable<ViewModels.User>> GetAsync(CancellationToken ct);
        Task<IEnumerable<ViewModels.User>> GetByTeamAsync(Guid teamId, CancellationToken ct);
        Task<IEnumerable<ViewModels.User>> GetByViewAsync(Guid viewId, CancellationToken ct);
        Task<ViewModels.User> GetAsync(Guid id, CancellationToken ct);
        Task<ViewModels.User> CreateAsync(ViewModels.User user, CancellationToken ct);
        Task<ViewModels.User> UpdateAsync(Guid id, ViewModels.User user, CancellationToken ct);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct);
        Task<bool> AddToTeamAsync(Guid teamId, Guid userId, CancellationToken ct);
        Task<bool> RemoveFromTeamAsync(Guid teamId, Guid userId, CancellationToken ct);
    }

    public class UserService : IUserService
    {
        private readonly PlayerContext _context;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private readonly IAuthorizationService _authorizationService;
        private readonly IUserClaimsService _userClaimsService;

        public UserService(PlayerContext context,
                            IPrincipal user,
                            IAuthorizationService authorizationService,
                            IUserClaimsService userClaimsService,
                            IMapper mapper)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _authorizationService = authorizationService;
            _userClaimsService = userClaimsService;
        }

        public async Task<IEnumerable<ViewModels.User>> GetAsync(CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement())).Succeeded)
                throw new ForbiddenException();

            var items = await _context.Users.ProjectTo<ViewModels.User>(_mapper.ConfigurationProvider).ToArrayAsync(ct);
            return items;
        }

        public async Task<IEnumerable<ViewModels.User>> GetByTeamAsync(Guid teamId, CancellationToken ct)
        {
            var team = await _context.Teams
                .Where(t => t.Id == teamId)
                .FirstOrDefaultAsync(ct);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new TeamAccessRequirement(team.ViewId, team.Id))).Succeeded)
                throw new ForbiddenException();

            var users = await _context.TeamMemberships
                .Where(t => t.TeamId == teamId)
                .Select(m => m.User)
                .Distinct()
                .ProjectTo<User>(_mapper.ConfigurationProvider)
                .ToArrayAsync(ct);

            return users;
        }

        public async Task<IEnumerable<ViewModels.User>> GetByViewAsync(Guid viewId, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(viewId))).Succeeded)
                throw new ForbiddenException();

            var view = await _context.Views
                .Where(e => e.Id == viewId)
                .FirstOrDefaultAsync(ct);

            if (view == null)
                throw new EntityNotFoundException<View>();

            var users = await _context.ViewMemberships
                .Where(m => m.ViewId == viewId)
                .Select(m => m.User)
                .Distinct()
                .ProjectTo<User>(_mapper.ConfigurationProvider)
                .ToArrayAsync(ct);

            return users;
        }

        public async Task<ViewModels.User> GetAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new UserAccessRequirement(id))).Succeeded)
                throw new ForbiddenException();

            var item = await _context.Users
                .ProjectTo<ViewModels.User>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id, ct);
            return item;
        }

        public async Task<ViewModels.User> CreateAsync(ViewModels.User user, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var userEntity = _mapper.Map<UserEntity>(user);

            _context.Users.Add(userEntity);
            await _context.SaveChangesAsync(ct);

            return await GetAsync(user.Id, ct);
        }

        public async Task<ViewModels.User> UpdateAsync(Guid id, ViewModels.User user, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            // Don't allow changing your own Id
            if (id == _user.GetId() && id != user.Id)
            {
                throw new ForbiddenException("You cannot change your own Id");
            }

            var userToUpdate = await _context.Users.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (userToUpdate == null)
                throw new EntityNotFoundException<User>();

            _mapper.Map(user, userToUpdate);

            _context.Users.Update(userToUpdate);
            await _context.SaveChangesAsync(ct);

            return await GetAsync(id, ct);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            if (id == _user.GetId())
            {
                throw new ForbiddenException("You cannot delete your own account");
            }

            var userToDelete = await _context.Users.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (userToDelete == null)
                throw new EntityNotFoundException<User>();

            _context.Users.Remove(userToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        public async Task<bool> AddToTeamAsync(Guid teamId, Guid userId, CancellationToken ct)
        {
            var team = await _context.Teams
                .Where(t => t.Id == teamId)
                .FirstOrDefaultAsync();

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var userExists = await _context.Users
                .Where(u => u.Id == userId)
                .AnyAsync(ct);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var viewIdQuery = _context.Teams.Where(t => t.Id == teamId).Select(t => t.ViewId);

            var viewMembership = await _context.ViewMemberships
                .Where(x => x.UserId == userId && viewIdQuery.Contains(x.ViewId))
                .SingleOrDefaultAsync(ct);

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(team.ViewId))).Succeeded)
                throw new ForbiddenException();

            bool setPrimary = false;
            if (viewMembership == null)
            {
                viewMembership = new ViewMembershipEntity { ViewId = team.ViewId, UserId = userId };
                _context.ViewMemberships.Add(viewMembership);
                await _context.SaveChangesAsync(ct);
                setPrimary = true;
            }

            var teamMembership = new TeamMembershipEntity { ViewMembershipId = viewMembership.Id, UserId = userId, TeamId = teamId };

            if (setPrimary)
            {
                viewMembership.PrimaryTeamMembership = teamMembership;
            }

            _context.TeamMemberships.Add(teamMembership);

            await _context.SaveChangesAsync(ct);
            await _userClaimsService.RefreshClaims(userId);

            return true;
        }

        public async Task<bool> RemoveFromTeamAsync(Guid teamId, Guid userId, CancellationToken ct)
        {
            var team = await _context.Teams
                .Where(t => t.Id == teamId)
                .FirstOrDefaultAsync(ct);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var userExists = await _context.Users
                .Where(u => u.Id == userId)
                .AnyAsync(ct);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(team.ViewId))).Succeeded)
                throw new ForbiddenException();

            var teamMemberships = await _context.TeamMemberships
                .Include(m => m.Team)
                .Where(m => m.UserId == userId)
                .ToArrayAsync(ct);

            var teamMembership = teamMemberships.SingleOrDefault(tu => tu.TeamId == teamId);

            if (teamMembership != null)
            {
                var viewMembership = _context.ViewMemberships.SingleOrDefault(eu => eu.UserId == userId && eu.ViewId == team.ViewId);

                if (teamMemberships.Where(m => m.Team.ViewId == team.ViewId).Count() == 1)
                {
                    _context.TeamMemberships.Remove(teamMembership);
                    viewMembership.PrimaryTeamMembershipId = null;
                    await _context.SaveChangesAsync();

                    _context.ViewMemberships.Remove(viewMembership);
                }
                else if (viewMembership.PrimaryTeamMembershipId == teamMembership.Id)
                {
                    // Set a new primary Team if we are deleting the current one
                    Guid newPrimaryTeamMembershipId = teamMemberships.Where(m => m.Team.ViewId == team.ViewId && m.TeamId != teamId).FirstOrDefault().Id;
                    viewMembership.PrimaryTeamMembershipId = newPrimaryTeamMembershipId;
                    _context.ViewMemberships.Update(viewMembership);
                    await _context.SaveChangesAsync(ct);

                    _context.TeamMemberships.Remove(teamMembership);
                }
                else
                {
                    _context.TeamMemberships.Remove(teamMembership);
                }

                await _context.SaveChangesAsync(ct);
            }

            await _userClaimsService.RefreshClaims(userId);
            return true;
        }
    }
}
