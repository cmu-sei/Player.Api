/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

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
using Z.EntityFramework.Plus;

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

        public TeamMembershipService(PlayerContext context, 
                                        IAuthorizationService authorizationService, 
                                        IPrincipal user,
                                        IMapper mapper)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
        }

        public async Task<TeamMembership> GetAsync(Guid id)
        {
            var item = await _context.TeamMemberships.SingleOrDefaultAsync(o => o.Id == id);

            var teamMembership = _mapper.Map<TeamMembership>(item);

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(teamMembership.ViewId, teamMembership.UserId))).Succeeded)
                throw new ForbiddenException();

            return teamMembership;
        }

        public async Task<IEnumerable<TeamMembership>> GetByViewIdForUserAsync(Guid viewId, Guid userId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SameUserOrViewAdminRequirement(viewId, userId))).Succeeded)
                throw new ForbiddenException();

            var userExists = _context.Users
                .Where(u => u.Id == userId)
                .DeferredAny()
                .FutureValue();

            var membershipQuery = _context.TeamMemberships
                .Where(m => m.UserId == userId && m.ViewMembership.ViewId == viewId)
                .Future();

            if (!(await userExists.ValueAsync()))
                throw new EntityNotFoundException<User>();

            return _mapper.Map<IEnumerable<TeamMembership>>(await membershipQuery.ToListAsync());
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

            return await GetAsync(id);
        }
    }
}
