// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.ViewModels.Webhooks;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Linq;
using System;

namespace Player.Api.Services
{
    public interface IWebhookService
    {
        Task<WebhookSubscription> Subscribe(WebhookSubscription form, CancellationToken ct);
        Task<IEnumerable<WebhookSubscription>> GetAll(CancellationToken ct);
        Task DeleteAsync(Guid id, CancellationToken ct);
    }

    public class WebhookService : IWebhookService
    {
        private readonly PlayerContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private IUserClaimsService _claimsService;

        public WebhookService(PlayerContext context, IAuthorizationService authorizationService, IPrincipal user, IMapper mapper, IUserClaimsService claimsService, IFileService fileService)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _claimsService = claimsService;
        }

        public async Task<WebhookSubscription> Subscribe(WebhookSubscription form, CancellationToken ct)
        {
            // Simply add the subscription to the db so it can be used later
            var entity = _mapper.Map<WebhookSubscriptionEntity>(form);
            _context.Webhooks.Add(entity);
            await _context.SaveChangesAsync();

            return form;  
        }

        public async Task<IEnumerable<WebhookSubscription>> GetAll(CancellationToken ct)
        {
            return await _context.Webhooks
                .ProjectTo<WebhookSubscription>(_mapper.ConfigurationProvider)
                .ToListAsync();
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct)
        {
            var toDelete = await _context.Webhooks
                .Where(w => w.Id == id)
                .SingleOrDefaultAsync();
            
            _context.Remove(toDelete);
            await _context.SaveChangesAsync();
        }
    }
}