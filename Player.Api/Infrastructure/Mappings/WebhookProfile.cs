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
            // CreateMap<Player.Api.Data.Data.Models.Webhooks.EventType, ViewModels.Webhooks.EventType>();
        }
    }
}