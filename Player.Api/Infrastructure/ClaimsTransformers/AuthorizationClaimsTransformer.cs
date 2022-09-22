// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Threading.Tasks;
using Player.Api.Services;
using Player.Api.Extensions;

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
            var user = principal.NormalizeScopeClaims();
            user = await _claimsService.AddUserClaims(user, true);
            _claimsService.SetCurrentClaimsPrincipal(user);
            return user;
        }
    }
}
