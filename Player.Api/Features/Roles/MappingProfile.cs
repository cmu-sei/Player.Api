// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using AutoMapper;
using Player.Api.Data.Data.Models;

namespace Player.Api.Features.Roles
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<RoleEntity, Role>()
                 .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src.Permissions.Select(x => x.Permission)));
            CreateMap<Create.Command, RoleEntity>();
            CreateMap<Edit.Command, RoleEntity>();
            CreateMap<SeedRole, RoleEntity>()
                .ForMember(dest => dest.Permissions, opt => opt.Ignore());
        }
    }
}
