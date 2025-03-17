// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Features.Teams;
using Player.Api.Features.Users;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Player.Api.Services;

public interface ITeamService
{
    Task<IEnumerable<Team>> GetByViewIdForCurrentUserAsync(Guid viewId, CancellationToken ct);
}

public class TeamService : ITeamService
{
    private readonly PlayerContext _context;
    private readonly IPlayerAuthorizationService _authorizationService;
    private readonly ClaimsPrincipal _user;
    private readonly IMapper _mapper;

    public TeamService(PlayerContext context, IPrincipal user, IPlayerAuthorizationService authorizationService, IMapper mapper)
    {
        _context = context;
        _authorizationService = authorizationService;
        _user = user as ClaimsPrincipal;
        _mapper = mapper;
    }

    public async Task<IEnumerable<Team>> GetByViewIdForCurrentUserAsync(Guid viewId, CancellationToken ct)
    {
        var userId = _user.GetId();

        var viewExists = await _context.Views
            .Where(e => e.Id == viewId)
            .AnyAsync(ct);

        if (!viewExists)
            throw new EntityNotFoundException<View>();

        var userExists = await _context.Users
            .Where(u => u.Id == userId)
            .AnyAsync(ct);

        if (!userExists)
            throw new EntityNotFoundException<User>();

        IQueryable<TeamDTO> teamQuery;

        if (await _authorizationService.Authorize<ViewEntity>(viewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], ct))
        {
            teamQuery = _context.Teams
                .Where(t => t.ViewId == viewId)
                .ProjectTo<TeamDTO>(_mapper.ConfigurationProvider);
        }
        else
        {
            teamQuery = _context.TeamMemberships
                .Where(x => x.UserId == userId && x.Team.ViewId == viewId)
                .Select(x => x.Team)
                .Distinct()
                .ProjectTo<TeamDTO>(_mapper.ConfigurationProvider);
        }

        var teams = await teamQuery.ToListAsync(ct);

        return _mapper.Map<IEnumerable<Team>>(teams);
    }
}
