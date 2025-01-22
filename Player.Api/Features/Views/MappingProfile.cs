// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Services;

namespace Player.Api.Features.Views
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<ViewEntity, View>();
            CreateMap<Create.Command, ViewEntity>();
            CreateMap<Edit.Command, ViewEntity>();
        }
    }
}
