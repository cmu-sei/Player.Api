// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Player.Api.Data.Data.Models;

namespace Player.Api.Features.TeamPermissions
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<TeamPermissionEntity, TeamPermissionModel>();
            CreateMap<Create.Command, TeamPermissionEntity>();
            CreateMap<Edit.Command, TeamPermissionEntity>();
        }
    }
}
