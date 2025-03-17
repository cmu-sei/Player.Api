// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security;
using Player.Api.Features.Permissions;
using Player.Api.Features.Roles;
using Player.Api.Features.TeamPermissions;
using Player.Api.Features.TeamRoles;
using Player.Api.Features.Users;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Options
{
    public class SeedDataOptions
    {
        public SeedRole[] Roles { get; set; } = [];
        public Features.Permissions.Create.Command[] Permissions { get; set; } = [];
        public SeedTeamRole[] TeamRoles { get; set; } = [];
        public Features.TeamPermissions.Create.Command[] TeamPermissions { get; set; } = [];
        public SeedUser[] Users { get; set; } = [];
        public WebhookSubscription[] Subscriptions { get; set; } = [];
    }
}
