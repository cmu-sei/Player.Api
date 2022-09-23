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
using Player.Api.Data.Data;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Services
{
    public interface IViewMembershipService
    {
        Task<ViewMembership> GetAsync(Guid id);
        Task<IEnumerable<ViewMembership>> GetByUserIdAsync(Guid userId);
    }

    public class ViewMembershipService : IViewMembershipService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public ViewMembershipService(PlayerContext context,
                                        IAuthorizationService authorizationService,
                                        IPrincipal user,
                                        IMapper mapper)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
        }

        public async Task<ViewMembership> GetAsync(Guid id)
        {
            var item = await _context.ViewMemberships
                .ProjectTo<ViewMembership>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id);

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(item.ViewId, item.UserId))).Succeeded)
                throw new ForbiddenException();

            return item;
        }

        public async Task<IEnumerable<ViewMembership>> GetByUserIdAsync(Guid userId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserRequirement(userId))).Succeeded)
                throw new ForbiddenException();

            var userExists = await _context.Users
                .Where(u => u.Id == userId)
                .AnyAsync();

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var memberships = await _context.ViewMemberships
                .Where(m => m.UserId == userId)
                .ProjectTo<ViewMembership>(_mapper.ConfigurationProvider)
                .ToArrayAsync();

            return memberships;
        }
    }
}
