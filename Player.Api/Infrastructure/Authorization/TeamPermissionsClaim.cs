/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Player.Api.Data.Data.Models;

namespace Player.Api.Infrastructure.Authorization;

public class TeamPermissionsClaim
{
    public Guid ViewId { get; set; }
    public Guid TeamId { get; set; }
    public bool IsPrimary { get; set; }
    public string[] PermissionValues { get; set; } = [];

    [JsonIgnore]
    public TeamPermission[] TeamPermissions
    {
        get
        {
            return PermissionValues
                .Where(x => Enum.TryParse<TeamPermission>(x, out var _))
                .Select(Enum.Parse<TeamPermission>)
                .ToArray();
        }
    }

    [JsonIgnore]
    public ViewPermission[] ViewPermissions
    {
        get
        {
            return PermissionValues
                .Where(x => Enum.TryParse<ViewPermission>(x, out var _))
                .Select(Enum.Parse<ViewPermission>)
                .ToArray();
        }
    }

    public TeamPermissionsClaim() { }

    public static TeamPermissionsClaim FromString(string json)
    {
        return JsonSerializer.Deserialize<TeamPermissionsClaim>(json);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}