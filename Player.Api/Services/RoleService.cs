// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Services
{
    public interface IRoleService
    {
        Task<IEnumerable<Role>> GetAsync();
        Task<Role> GetAsync(Guid id);
        Task<Role> GetAsync(string name);
        Task<Role> CreateAsync(RoleForm form);
        Task<Role> UpdateAsync(Guid id, RoleForm form);
        Task<bool> DeleteAsync(Guid id);
    }

    public class RoleService : IRoleService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public RoleService(PlayerContext context,
                            IAuthorizationService authorizationService,
                            IPrincipal user,
                            IMapper mapper)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
        }

        public async Task<IEnumerable<Role>> GetAsync()
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement())).Succeeded)
                throw new ForbiddenException();

            var items = await _context.Roles
                .ProjectTo<Role>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return items;
        }

        public async Task<Role> GetAsync(Guid id)
        {
            var item = await _context.Roles
                .ProjectTo<Role>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id);

            return item;
        }

        public async Task<Role> GetAsync(string name)
        {
            var item = await _context.Roles
                .ProjectTo<Role>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Name == name);

            if (item == null)
            {
                throw new EntityNotFoundException<Role>();
            }

            return item;
        }

        public async Task<Role> CreateAsync(RoleForm form)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            // Ensure role with this name does not already exist
            var role = await _context.Roles
                .ProjectTo<Role>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Name == form.Name);

            if (role != null)
            {
                throw new ConflictException("A role with that name already exists.");
            }

            var roleEntity = _mapper.Map<RoleEntity>(form);

            _context.Roles.Add(roleEntity);
            await _context.SaveChangesAsync();

            return _mapper.Map<Role>(roleEntity);
        }

        public async Task<Role> UpdateAsync(Guid id, RoleForm form)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var roleToUpdate = await _context.Roles.SingleOrDefaultAsync(v => v.Id == id);

            if (roleToUpdate == null)
                throw new EntityNotFoundException<Role>();

            _mapper.Map(form, roleToUpdate);

            _context.Roles.Update(roleToUpdate);
            await _context.SaveChangesAsync();

            return await GetAsync(roleToUpdate.Id);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var roleToDelete = await _context.Roles.SingleOrDefaultAsync(t => t.Id == id);

            if (roleToDelete == null)
                throw new EntityNotFoundException<Role>();

            _context.Roles.Remove(roleToDelete);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}
