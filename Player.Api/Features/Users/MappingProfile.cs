// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using AutoMapper;
using Player.Api.Data.Data.Models;

namespace Player.Api.Features.Users
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<UserEntity, User>()
                .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.RoleId.HasValue ? src.Role.Name : null));

            CreateMap<Create.Command, UserEntity>();
            CreateMap<Edit.Command, UserEntity>();
            CreateMap<SeedUser, UserEntity>()
                .ForMember(dest => dest.Role, opt => opt.Ignore());
        }
    }
}
