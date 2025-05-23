// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Player.Api.Features.TeamMemberships
{
    public class TeamMembership
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public string UserName { get; set; }

        public Guid TeamId { get; set; }
        public string TeamName { get; set; }

        public Guid? RoleId { get; set; }
        public string RoleName { get; set; }

        public Guid ViewId { get; set; }

        public bool isPrimary { get; set; }
    }
}
