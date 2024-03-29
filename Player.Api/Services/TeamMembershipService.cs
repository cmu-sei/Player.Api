// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
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
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Services
{
    public interface ITeamMembershipService
    {
        Task<TeamMembership> GetAsync(Guid id);
        Task<IEnumerable<TeamMembership>> GetByViewIdForUserAsync(Guid viewId, Guid userId);
        Task<TeamMembership> UpdateAsync(Guid id, TeamMembershipForm form);
    }

    public class TeamMembershipService : ITeamMembershipService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private ILogger<ITeamMembershipService> _logger;

        public TeamMembershipService(PlayerContext context,
                                        IAuthorizationService authorizationService,
                                        IPrincipal user,
                                        IMapper mapper,
                                        ILogger<ITeamMembershipService> logger)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<TeamMembership> GetAsync(Guid id)
        {
            var item = await _context.TeamMemberships
                .ProjectTo<TeamMembership>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id);

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(item.ViewId, item.UserId))).Succeeded)
                throw new ForbiddenException();

            return item;
        }

        public async Task<IEnumerable<TeamMembership>> GetByViewIdForUserAsync(Guid viewId, Guid userId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(viewId, userId))).Succeeded)
                throw new ForbiddenException();

            var userExists = await _context.Users
                .Where(u => u.Id == userId)
                .AnyAsync();

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var memberships = await _context.TeamMemberships
                .Where(m => m.UserId == userId && m.ViewMembership.ViewId == viewId)
                .ProjectTo<TeamMembership>(_mapper.ConfigurationProvider)
                .ToArrayAsync();

            return memberships;
        }

        public async Task<TeamMembership> UpdateAsync(Guid id, TeamMembershipForm form)
        {
            var membershipToUpdate = await _context.TeamMemberships
                .Include(m => m.ViewMembership)
                .SingleOrDefaultAsync(v => v.Id == id);

            if (membershipToUpdate == null)
                throw new EntityNotFoundException<TeamMembership>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(membershipToUpdate.ViewMembership.ViewId))).Succeeded)
                throw new ForbiddenException();

            _mapper.Map(form, membershipToUpdate);

            _context.TeamMemberships.Update(membershipToUpdate);
            await _context.SaveChangesAsync();
            _logger.LogWarning($"Team Membership updated by {_user.GetId()} = User: {membershipToUpdate.UserId}, Role: {membershipToUpdate.RoleId}, Team: {membershipToUpdate.TeamId}, ViewMembership: {membershipToUpdate.ViewMembershipId}");
            return await GetAsync(id);
        }
    }
}
