/*
Copyright 2022 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.ViewModels.Webhooks;
using System.Linq;

namespace Player.Api.Infrastructure.Mappings
{
    public class WebhookProfile : AutoMapper.Profile
    {
        public WebhookProfile()
        {
            CreateMap<WebhookSubscription, WebhookSubscriptionEntity>()
                .ForMember(dest => dest.EventTypes, opt => opt.MapFrom(src => src.EventTypes.Select(et => new WebhookSubscriptionEventTypeEntity(Guid.Empty, et))));

            CreateMap<WebhookSubscriptionEntity, WebhookSubscription>()
                .ForMember(dest => dest.EventTypes, opt => opt.MapFrom(src => src.EventTypes.Select(x => x.EventType)));

            CreateMap<WebhookSubscriptionForm, WebhookSubscriptionEntity>()
                .ForMember(dest => dest.EventTypes, opt => opt.MapFrom(src => src.EventTypes.Select(et => new WebhookSubscriptionEventTypeEntity(Guid.Empty, et))));

            CreateMap<WebhookSubscriptionPartialEditForm, WebhookSubscriptionEntity>()
                .ForMember(dest => dest.EventTypes, opt => opt.MapFrom(src => src.EventTypes.Select(et => new WebhookSubscriptionEventTypeEntity(Guid.Empty, et))))
                .ForMember(dest => dest.EventTypes, opts => opts.PreCondition((src) => src.EventTypes != null))
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}