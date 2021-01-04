// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;
using System.Net;

namespace Player.Api.Infrastructure.Mappings
{
    public class ApplicationProfile : AutoMapper.Profile
    {
        public ApplicationProfile()
        {
            CreateMap<ApplicationTemplateEntity, ApplicationTemplate>().ReverseMap();

            CreateMap<ApplicationTemplateForm, ApplicationTemplateEntity>();

            CreateMap<ApplicationEntity, Application>().ReverseMap();

            CreateMap<ApplicationInstanceEntity, ApplicationInstanceForm>().ReverseMap();

            CreateMap<ApplicationInstanceEntity, ApplicationInstance>()
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src =>
                    (src.Application.Name ?? src.Application.Template.Name ?? string.Empty)
                        .Replace("{viewId}", src.Application.ViewId.ToString())
                        .Replace("{viewName}", src.Application.View.Name ?? string.Empty)
                        .Replace("{teamId}", src.TeamId.ToString())
                        .Replace("{teamName}", src.Team.Name ?? string.Empty)))

                .ForMember(dest => dest.Icon, opt => opt.MapFrom(src =>
                    src.Application.Icon ?? (src.Application.Template != null ? src.Application.Template.Icon : string.Empty)))

                .ForMember(dest => dest.Embeddable, opt => opt.MapFrom(src =>
                    src.Application.Embeddable ?? (src.Application.Template != null ? src.Application.Template.Embeddable : false)))

                .ForMember(dest => dest.LoadInBackground, opt => opt.MapFrom(src =>
                    src.Application.LoadInBackground ?? (src.Application.Template != null ? src.Application.Template.LoadInBackground : false)))

                .ForMember(dest => dest.Url, opt => opt.MapFrom(src =>
                    (src.Application.Url ?? src.Application.Template.Url ?? string.Empty)
                        .Replace("{viewId}", src.Application.ViewId.ToString())
                        .Replace("{viewName}", src.Application.View.Name != null ? WebUtility.UrlEncode(src.Application.View.Name) : string.Empty)
                        .Replace("{teamId}", src.TeamId.ToString())
                        .Replace("{teamName}", src.Team.Name != null ? WebUtility.UrlEncode(src.Team.Name) : string.Empty)))

                .ForMember(dest => dest.ViewId, opt => opt.MapFrom(src =>
                    src.Application.ViewId));
        }
    }
}
