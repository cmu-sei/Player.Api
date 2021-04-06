using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class WebHookSubscription
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string CallbackUri { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public List<EventType> EventTypes { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EventType
    {
        ViewCreated,
        ViewDeleted
    }
}