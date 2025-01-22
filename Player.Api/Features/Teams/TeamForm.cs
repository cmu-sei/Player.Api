// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Player.Api.Features.Teams;

public class TeamForm
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid? RoleId { get; set; }
}

