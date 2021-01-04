// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;

namespace Player.Api.Infrastructure.Mappings
{
    public class NotificationProfile : AutoMapper.Profile
    {
        public NotificationProfile()
        {
            CreateMap<NotificationEntity, Notification>().ReverseMap();

            CreateMap<NotificationEntity, Notification>()
            .ForMember(x => x.WasSuccess, opt => opt.Ignore())
            .ForMember(x => x.CanPost, opt => opt.Ignore())
            .ForMember(x => x.IconUrl, opt => opt.Ignore());
        }
    }
}
