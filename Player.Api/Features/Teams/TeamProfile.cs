// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Services;
using System.Linq;
using System.Security.Claims;

namespace Player.Api.Features.Teams;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Team, TeamEntity>();
        CreateMap<TeamForm, TeamEntity>();

        CreateMap<Create.Command, TeamEntity>();
        CreateMap<Edit.Command, TeamEntity>();

        CreateMap<TeamEntity, TeamDTO>()
            .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.Role.Name))
            .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src.Permissions.Select(x => x.Permission)));

        CreateMap<TeamDTO, Team>()
            .ForMember(dest => dest.IsMember, opt => opt.MapFrom<TeamMemberResolver>())
            .ForMember(dest => dest.IsPrimary, opt => opt.MapFrom<PrimaryTeamResolver>());
    }
}

public class TeamMemberResolver : IValueResolver<TeamDTO, Team, bool>
{
    private IAuthorizationService _authorizationService;
    private ClaimsPrincipal _user;

    public TeamMemberResolver(IAuthorizationService authorizationService, IUserClaimsService userClaimsService)
    {
        _authorizationService = authorizationService;
        _user = userClaimsService.GetCurrentClaimsPrincipal();
    }

    public bool Resolve(TeamDTO source, Team destination, bool member, ResolutionContext context)
    {
        return _authorizationService.AuthorizeAsync(_user, null, new TeamMemberRequirement(source.Id)).Result.Succeeded;
    }
}

public class PrimaryTeamResolver : IValueResolver<TeamDTO, Team, bool>
{
    private IAuthorizationService _authorizationService;
    private ClaimsPrincipal _user;

    public PrimaryTeamResolver(IAuthorizationService authorizationService, IUserClaimsService userClaimsService)
    {
        _authorizationService = authorizationService;
        _user = userClaimsService.GetCurrentClaimsPrincipal();
    }

    public bool Resolve(TeamDTO source, Team destination, bool member, ResolutionContext context)
    {
        return _authorizationService.AuthorizeAsync(_user, null, new PrimaryTeamRequirement(source.Id)).Result.Succeeded;
    }
}