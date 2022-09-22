// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;

namespace Player.Api.Infrastructure.Mappings
{
    public class TeamMembershipProfile : AutoMapper.Profile
    {
        public TeamMembershipProfile()
        {
            CreateMap<TeamMembershipEntity, TeamMembership>()
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.Name))
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team.Name))
                .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.RoleId.HasValue ? src.Role.Name : null))
                .ForMember(dest => dest.ViewId, opt => opt.MapFrom(src => src.ViewMembership.ViewId))
                .ForMember(dest => dest.isPrimary, opt => opt.MapFrom(src => src.ViewMembership.PrimaryTeamMembershipId == src.Id));

            CreateMap<TeamMembershipForm, TeamMembershipEntity>();
        }
    }
}
