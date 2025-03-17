// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;

namespace Player.Api.Features.Permissions
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<PermissionEntity, Permission>();
            CreateMap<Create.Command, PermissionEntity>();
            CreateMap<Edit.Command, PermissionEntity>();
        }
    }
}
