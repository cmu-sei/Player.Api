// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.ViewModels;
using System.Linq;

namespace Player.Api.Infrastructure.Mappings
{
    public class UserProfile : AutoMapper.Profile
    {
        public UserProfile()
        {
            CreateMap<User, UserEntity>()
                .ForMember(dest => dest.Permissions, opt => opt.Ignore());

            CreateMap<UserEntity, User>()
                .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.RoleId.HasValue ? src.Role.Name : null))
                .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src.Permissions.Select(x => x.Permission)))
                .ForMember(dest => dest.IsSystemAdmin, opt => opt.MapFrom(src => (src.Permissions.Where(p => p.Permission.Key == PlayerClaimTypes.SystemAdmin.ToString()).Any()) ||
                    src.Role.Permissions.Where(p => p.Permission.Key == PlayerClaimTypes.SystemAdmin.ToString()).Any()));
        }
    }
}
