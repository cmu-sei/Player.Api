// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;
using System.Linq;

namespace Player.Api.Infrastructure.Mappings
{
    public class RoleProfile : AutoMapper.Profile
    {
        public RoleProfile()
        {
            CreateMap<RoleEntity, Role>()
                .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src.Permissions.Select(x => x.Permission)));

            CreateMap<RoleForm, RoleEntity>();
        }
    }
}
