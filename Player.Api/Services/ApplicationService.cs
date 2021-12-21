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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Services
{
    public interface IApplicationService
    {
        // Application Templates
        Task<IEnumerable<ViewModels.ApplicationTemplate>> GetTemplatesAsync(CancellationToken ct);
        Task<ViewModels.ApplicationTemplate> GetTemplateAsync(Guid id, CancellationToken ct);
        Task<ViewModels.ApplicationTemplate> CreateTemplateAsync(ViewModels.ApplicationTemplateForm form, CancellationToken ct);
        Task<ViewModels.ApplicationTemplate> UpdateTemplateAsync(Guid id, ViewModels.ApplicationTemplateForm form, CancellationToken ct);
        Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct);

        // Applications
        Task<IEnumerable<ViewModels.Application>> GetApplicationsByViewAsync(Guid viewId, CancellationToken ct);
        Task<ViewModels.Application> GetApplicationAsync(Guid id, CancellationToken ct);
        Task<ViewModels.Application> CreateApplicationAsync(Guid viewId, ViewModels.Application application, CancellationToken ct);
        Task<ViewModels.Application> UpdateApplicationAsync(Guid id, ViewModels.Application application, CancellationToken ct);
        Task<bool> DeleteApplicationAsync(Guid id, CancellationToken ct);

        // Application Instances
        Task<IEnumerable<ViewModels.ApplicationInstance>> GetInstancesByTeamAsync(Guid teamId, CancellationToken ct);
        Task<ViewModels.ApplicationInstance> GetInstanceAsync(Guid id, CancellationToken ct);
        Task<ViewModels.ApplicationInstance> CreateInstanceAsync(Guid teamId, ViewModels.ApplicationInstanceForm instance, CancellationToken ct);
        Task<ViewModels.ApplicationInstance> UpdateInstanceAsync(Guid id, ViewModels.ApplicationInstanceForm instance, CancellationToken ct);
        Task<bool> DeleteInstanceAsync(Guid id, CancellationToken ct);
    }

    public class ApplicationService : IApplicationService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public ApplicationService(PlayerContext context,
                                  IAuthorizationService authorizationService,
                                  IPrincipal user,
                                  IMapper mapper)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
        }

        #region Application Templates

        public async Task<IEnumerable<ViewModels.ApplicationTemplate>> GetTemplatesAsync(CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement())).Succeeded)
                throw new ForbiddenException();

            var items = await _context.ApplicationTemplates
                .ProjectTo<ViewModels.ApplicationTemplate>(_mapper.ConfigurationProvider)
                .ToListAsync(ct);

            return items;
        }

        public async Task<ViewModels.ApplicationTemplate> GetTemplateAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement())).Succeeded)
                throw new ForbiddenException();

            var item = await _context.ApplicationTemplates
                .ProjectTo<ViewModels.ApplicationTemplate>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            return item;
        }

        public async Task<ViewModels.ApplicationTemplate> CreateTemplateAsync(ViewModels.ApplicationTemplateForm form, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var templateEntity = _mapper.Map<ApplicationTemplateEntity>(form);

            _context.ApplicationTemplates.Add(templateEntity);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map<ViewModels.ApplicationTemplate>(templateEntity);
        }

        public async Task<ViewModels.ApplicationTemplate> UpdateTemplateAsync(Guid id, ViewModels.ApplicationTemplateForm form, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var templateToUpdate = await _context.ApplicationTemplates.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (templateToUpdate == null)
                throw new EntityNotFoundException<ApplicationTemplate>();

            _mapper.Map(form, templateToUpdate);

            _context.ApplicationTemplates.Update(templateToUpdate);
            await _context.SaveChangesAsync(ct);

            return await GetTemplateAsync(templateToUpdate.Id, ct);
        }

        public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var templateToDelete = await _context.ApplicationTemplates.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (templateToDelete == null)
                throw new EntityNotFoundException<ApplicationTemplate>();

            _context.ApplicationTemplates.Remove(templateToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        #endregion

        #region Applications

        public async Task<IEnumerable<ViewModels.Application>> GetApplicationsByViewAsync(Guid viewId, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(viewId))).Succeeded)
                throw new ForbiddenException();

            var view = await _context.Views
                .Include(e => e.Applications)
                .Where(e => e.Id == viewId)
                .SingleOrDefaultAsync(ct);

            if (view == null)
                throw new EntityNotFoundException<View>();

            return _mapper.Map<IEnumerable<ViewModels.Application>>(view.Applications);
        }

        public async Task<ViewModels.Application> GetApplicationAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.Applications
                .ProjectTo<ViewModels.Application>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(item.ViewId))).Succeeded)
                throw new ForbiddenException();

            return item;
        }

        public async Task<ViewModels.Application> CreateApplicationAsync(Guid viewId, ViewModels.Application application, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(viewId))).Succeeded)
                throw new ForbiddenException();

            var viewExists = await _context.Views.Where(e => e.Id == viewId).AnyAsync(ct);

            if (!viewExists)
                throw new EntityNotFoundException<View>();

            var applicationEntity = _mapper.Map<ApplicationEntity>(application);

            _context.Applications.Add(applicationEntity);
            await _context.SaveChangesAsync(ct);

            application = _mapper.Map<ViewModels.Application>(applicationEntity);

            return application;
        }

        public async Task<ViewModels.Application> UpdateApplicationAsync(Guid id, ViewModels.Application application, CancellationToken ct)
        {
            var applicationToUpdate = await _context.Applications.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (applicationToUpdate == null)
                throw new EntityNotFoundException<Application>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(application.ViewId))).Succeeded)
                throw new ForbiddenException();

            _mapper.Map(application, applicationToUpdate);

            _context.Applications.Update(applicationToUpdate);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map(applicationToUpdate, application);
        }

        public async Task<bool> DeleteApplicationAsync(Guid id, CancellationToken ct)
        {
            var applicationToDelete = await _context.Applications.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (applicationToDelete == null)
                throw new EntityNotFoundException<Application>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(applicationToDelete.ViewId))).Succeeded)
                throw new ForbiddenException();

            _context.Applications.Remove(applicationToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        #endregion

        #region Application Instances

        public async Task<IEnumerable<ViewModels.ApplicationInstance>> GetInstancesByTeamAsync(Guid teamId, CancellationToken ct)
        {
            var team = await _context.Teams
                .Where(e => e.Id == teamId)
                .SingleOrDefaultAsync(ct);

            var instances = await _context.ApplicationInstances
                .Where(i => i.TeamId == teamId)
                .OrderBy(a => a.DisplayOrder)
                .ProjectTo<ViewModels.ApplicationInstance>(_mapper.ConfigurationProvider)
                .ToArrayAsync(ct);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new TeamAccessRequirement(team.ViewId, teamId))).Succeeded)
                throw new ForbiddenException();

            return instances;
        }

        public async Task<ViewModels.ApplicationInstance> GetInstanceAsync(Guid id, CancellationToken ct)
        {
            var instance = await _context.ApplicationInstances
                .ProjectTo<ViewModels.ApplicationInstance>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(a => a.Id == id, ct);

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(instance.ViewId))).Succeeded)
                throw new ForbiddenException();

            return instance;
        }

        public async Task<ViewModels.ApplicationInstance> CreateInstanceAsync(Guid teamId, ViewModels.ApplicationInstanceForm form, CancellationToken ct)
        {
            var team = await _context.Teams.Where(e => e.Id == teamId).SingleOrDefaultAsync(ct);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(team.ViewId))).Succeeded)
                throw new ForbiddenException();

            var instanceEntity = _mapper.Map<ApplicationInstanceEntity>(form);

            _context.ApplicationInstances.Add(instanceEntity);
            await _context.SaveChangesAsync(ct);

            var instance = await _context.ApplicationInstances
                .ProjectTo<ViewModels.ApplicationInstance>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(i => i.Id == instanceEntity.Id, ct);

            return instance;
        }

        public async Task<ViewModels.ApplicationInstance> UpdateInstanceAsync(Guid id, ViewModels.ApplicationInstanceForm form, CancellationToken ct)
        {
            var instanceToUpdate = await _context.ApplicationInstances
                .Include(ai => ai.Team)
                .SingleOrDefaultAsync(v => v.Id == id, ct);

            if (instanceToUpdate == null)
                throw new EntityNotFoundException<ApplicationInstance>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(instanceToUpdate.Team.ViewId))).Succeeded)
                throw new ForbiddenException();

            _mapper.Map(form, instanceToUpdate);

            _context.ApplicationInstances.Update(instanceToUpdate);
            await _context.SaveChangesAsync(ct);

            var instance = await _context.ApplicationInstances
                .ProjectTo<ViewModels.ApplicationInstance>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(i => i.Id == instanceToUpdate.Id, ct);

            return instance;
        }

        public async Task<bool> DeleteInstanceAsync(Guid id, CancellationToken ct)
        {
            var instanceToDelete = await _context.ApplicationInstances
                .Include(ai => ai.Team)
                .SingleOrDefaultAsync(v => v.Id == id, ct);

            if (instanceToDelete == null)
                throw new EntityNotFoundException<ApplicationInstance>();

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(instanceToDelete.Team.ViewId))).Succeeded)
                throw new ForbiddenException();

            _context.ApplicationInstances.Remove(instanceToDelete);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        #endregion
    }
}
