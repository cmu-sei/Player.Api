// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using Player.Api.Data.Data;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Player.Api.Extensions;
using Z.EntityFramework.Plus;
using Player.Api.Data.Data.Models;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Options;
using Microsoft.EntityFrameworkCore;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Services;

namespace Player.Api.Infrastructure.ClaimsTransformers
{
    class AuthorizationClaimsTransformer : IClaimsTransformation
    {
        private IUserClaimsService _claimsService;

        public AuthorizationClaimsTransformer(IUserClaimsService claimsService)
        {
            _claimsService = claimsService;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var user = await _claimsService.AddUserClaims(principal, true);
            _claimsService.SetCurrentClaimsPrincipal(user);
            return user;
        }
    }
}
