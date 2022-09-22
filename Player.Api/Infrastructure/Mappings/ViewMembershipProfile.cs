// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;

namespace Player.Api.Infrastructure.Mappings
{
    public class ViewMembershipProfile : AutoMapper.Profile
    {
        public ViewMembershipProfile()
        {
            CreateMap<ViewMembershipEntity, ViewMembership>()
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.Name))
                .ForMember(dest => dest.ViewName, opt => opt.MapFrom(src => src.View.Name))
                .ForMember(dest => dest.PrimaryTeamId, opt => opt.MapFrom(src => src.PrimaryTeamMembership.TeamId))
                .ForMember(dest => dest.PrimaryTeamName, opt => opt.MapFrom(src => src.PrimaryTeamMembership.Team.Name));
        }
    }
}
