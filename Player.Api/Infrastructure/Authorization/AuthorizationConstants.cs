/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

namespace Player.Api.Infrastructure.Authorization;

public static class AuthorizationConstants
{
    public const string PermissionsClaimType = "Permission";
    public const string ViewPermissionsClaimType = "ViewPermission";
    public const string TeamPermissionsClaimType = "TeamPermission";
}