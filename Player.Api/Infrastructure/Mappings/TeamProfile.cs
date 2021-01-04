// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Services;
using Player.Api.ViewModels;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Mappings
{
    public class TeamProfile : AutoMapper.Profile
    {
        public TeamProfile()
        {
            CreateMap<Team, TeamEntity>();
            CreateMap<TeamForm, TeamEntity>();

            CreateMap<TeamEntity, TeamDTO>()
                .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.RoleId.HasValue ? src.Role.Name : null))
                .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src.Permissions.Select(x => x.Permission)));

            CreateMap<TeamDTO, Team>()
                .ForMember(dest => dest.CanManage, opt => opt.MapFrom<ManageTeamResolver>())
                .ForMember(dest => dest.IsMember, opt => opt.MapFrom<TeamMemberResolver>())
                .ForMember(dest => dest.IsPrimary, opt => opt.MapFrom<PrimaryTeamResolver>());
        }
    }

    public class ManageTeamResolver : IValueResolver<TeamDTO, Team, bool>
    {
        private IAuthorizationService _authorizationService;
        private ClaimsPrincipal _user;

        public ManageTeamResolver(IAuthorizationService authorizationService, IUserClaimsService userClaimsService)
        {
            _authorizationService = authorizationService;
            _user = userClaimsService.GetCurrentClaimsPrincipal();
        }

        public bool Resolve(TeamDTO source, Team destination, bool member, ResolutionContext context)
        {
            return _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(source.ViewId)).Result.Succeeded;
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
}
