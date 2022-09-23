// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Services;
using Player.Api.ViewModels;
using System.Security.Claims;

namespace Player.Api.Infrastructure.Mappings
{
    public class ViewProfile : AutoMapper.Profile
    {
        public ViewProfile()
        {
            CreateMap<ViewEntity, View>()
                .ForMember(dest => dest.CanManage, opt => opt.MapFrom<ManageViewResolver>());

            CreateMap<View, ViewEntity>();

            CreateMap<ViewForm, ViewEntity>();
        }
    }

    public class ManageViewResolver : IValueResolver<ViewEntity, View, bool>
    {
        private IAuthorizationService _authorizationService;
        private ClaimsPrincipal _user;

        public ManageViewResolver(IAuthorizationService authorizationService, IUserClaimsService userClaimsService)
        {
            _authorizationService = authorizationService;
            _user = userClaimsService.GetCurrentClaimsPrincipal();
        }

        public bool Resolve(ViewEntity source, View destination, bool member, ResolutionContext context)
        {
            return _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(source.Id)).Result.Succeeded;
        }
    }
}
