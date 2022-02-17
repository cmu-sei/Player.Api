// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using System;

namespace Player.Api.ViewModels
{
    public class Notification
    {
        public int Key { get; set; }
        public Guid FromId { get; set; }
        public NotificationType FromType { get; set; }
        public Guid ToId { get; set; }
        public NotificationType ToType { get; set; }
        public DateTime BroadcastTime { get; set; }
        public string ToName { get; set; }
        public string FromName { get; set; }
        public string Subject { get; set; }
        public string Text { get; set; }
        public string Link { get; set; }
        public NotificationPriority Priority { get; set; }
        public bool WasSuccess { get; set; }
        public bool CanPost { get; set; }
        public string IconUrl { get; set; }

        public bool IsValid()
        {
            var isValid = false;
            if (Text != null && Text.Length > 0)
            {
                isValid = true;
            }
            return isValid;
        }
    }

}
