// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Player.Api.Features.ViewMemberships
{
    public class ViewMembership
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; }
        public Guid ViewId { get; set; }
        public string ViewName { get; set; }
        public Guid PrimaryTeamId { get; set; }
        public string PrimaryTeamName { get; set; }
    }
}
