// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Player.Api.Data.Data.Models;
using System;

namespace Player.Api.Features.Applications
{
    public class MappingProfile : Profile
    {
        /// <summary>
        /// The application maps still on AutoMapper: the Views export and import nest them. The feature
        /// itself maps through <see cref="ApplicationMapper"/>, and these move to Mapperly with Views.
        /// </summary>
        public MappingProfile()
        {
            CreateMap<ApplicationExport, ApplicationEntity>();
            CreateMap<ApplicationEntity, ApplicationExport>()
                .ForMember(dest => dest.ApplicationTemplateName, opt => opt.MapFrom(src => src.Template != null ? src.Template.Name : null));
            CreateMap<ApplicationInstanceExport, ApplicationInstanceEntity>().ReverseMap();
        }
    }
}
