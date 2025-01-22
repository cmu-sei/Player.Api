// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json.Serialization;

namespace Player.Api.Infrastructure.Authorization
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlayerClaimTypes
    {
        TeamMember,
        ViewMember,
        PrimaryTeam,
    }
}
