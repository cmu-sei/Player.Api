// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models
{
    public class NotificationEntity : IEntity
    {
        [Key]
        public int Key { get; set; }
        public Guid? ViewId { get; set; }
        public string FromName { get; set; }
        public Guid FromId { get; set; }
        public NotificationType FromType { get; set; }
        public string ToName { get; set; }
        public Guid ToId { get; set; }
        public NotificationType ToType { get; set; }
        public string Subject { get; set; }
        public string Text { get; set; }
        public string Link { get; set; }
        public DateTime BroadcastTime { get; set; }
        public NotificationPriority Priority { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationPriority
    {
        Normal = 0,
        Elevated = 1,
        High = 2,
        System = 3
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationType
    {
        View = 0,
        Team = 1,
        User = 2,
        Application = 3,
        Admin = 4
    }
}
