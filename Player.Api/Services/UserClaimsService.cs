/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Options;
using Player.Api.ViewModels;
using Z.EntityFramework.Plus;

namespace Player.Api.Services
{
    public interface IUserClaimsService
    {
        Task<ClaimsPrincipal> AddUserClaims(ClaimsPrincipal principal, bool update);
        Task<ClaimsPrincipal> GetClaimsPrincipal(Guid userId, bool setAsCurrent);
        Task<ClaimsPrincipal> RefreshClaims(Guid userId);
        ClaimsPrincipal GetCurrentClaimsPrincipal();
        void SetCurrentClaimsPrincipal(ClaimsPrincipal principal);
    }

    public class UserClaimsService : IUserClaimsService
    {
        private readonly PlayerContext _context;
        private readonly ClaimsTransformationOptions _options;
        private IMemoryCache _cache;
        private ClaimsPrincipal _currentClaimsPrincipal;
        private readonly IMapper _mapper;

        public UserClaimsService(PlayerContext context, 
                                    IMemoryCache cache, 
                                    ClaimsTransformationOptions options,
                                    IMapper mapper)
        {
            _context = context;
            _options = options;
            _cache = cache;
            _mapper = mapper;
        }

        public async Task<ClaimsPrincipal> AddUserClaims(ClaimsPrincipal principal, bool update)
        {
            List<Claim> claims;
            var identity = ((ClaimsIdentity)principal.Identity);
            var userId = principal.GetId();

            if (!_cache.TryGetValue(userId, out claims))
            {
                claims = new List<Claim>();
                var user = await ValidateUser(userId, principal.FindFirst("name")?.Value, update);

                if (user != null)
                {
                    claims.AddRange(await GetUserClaims(userId));

                    if (_options.EnableCaching)
                    {
                        _cache.Set(userId, claims, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheExpirationSeconds)));
                    }
                }
            }
            addNewClaims(identity, claims);
            return principal;
        }

        public async Task<ClaimsPrincipal> GetClaimsPrincipal(Guid userId, bool setAsCurrent)
        {
            ClaimsIdentity identity = new ClaimsIdentity();
            identity.AddClaim(new Claim("sub", userId.ToString()));
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            principal = await AddUserClaims(principal, false);

            if (setAsCurrent || _currentClaimsPrincipal.GetId() == userId)
            {
                _currentClaimsPrincipal = principal;
            }

            return principal;
        }

        public async Task<ClaimsPrincipal> RefreshClaims(Guid userId)
        {
            _cache.Remove(userId);
            return await GetClaimsPrincipal(userId, false);
        }

        public ClaimsPrincipal GetCurrentClaimsPrincipal()
        {
            return _currentClaimsPrincipal;
        }

        public void SetCurrentClaimsPrincipal(ClaimsPrincipal principal)
        {
            _currentClaimsPrincipal = principal;
        }

        private async Task<UserEntity> ValidateUser(Guid subClaim, string nameClaim, bool update)
        {
            var userQuery = _context.Users.Where(u => u.Id == subClaim).Future();
            var anyUsers = _context.Users.DeferredAny().FutureValue();
            var user = (await userQuery.ToListAsync()).SingleOrDefault();

            if (update)
            {
                if (user == null)
                {
                    user = new UserEntity
                    {
                        Id = subClaim,
                        Name = nameClaim ?? "Anonymous"
                    };

                    // First user is default SystemAdmin
                    if (!(await anyUsers.ValueAsync()))
                    {
                        var systemAdminPermission = await _context.Permissions.Where(p => p.Key == PlayerClaimTypes.SystemAdmin.ToString()).FirstOrDefaultAsync();

                        if (systemAdminPermission != null)
                        {
                            user.Permissions.Add(new UserPermissionEntity(user.Id, systemAdminPermission.Id));
                        }
                    }

                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    if (nameClaim != null && user.Name != nameClaim)
                    {
                        user.Name = nameClaim;
                        _context.Update(user);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            return user;
        }

        private async Task<IEnumerable<Claim>> GetUserClaims(Guid userId)
        {
            List<Claim> claims = new List<Claim>();

            var userDetails = await _context.Users
                .Include(u => u.Role)
                .ThenInclude(r => r.Permissions)  
                .ThenInclude(p => p.Permission)
                .Include(u => u.Permissions)
                .ThenInclude(p => p.Permission)
                .Include(u => u.TeamMemberships)
                .ThenInclude(t => t.Role)
                .ThenInclude(r => r.Permissions)
                .ThenInclude(p => p.Permission)
                .Include(u => u.TeamMemberships)
                .ThenInclude(t => t.Team)
                .ThenInclude(t => t.Role)
                .ThenInclude(r => r.Permissions)
                .ThenInclude(p => p.Permission)
                .Include(u => u.TeamMemberships)
                .ThenInclude(t => t.ViewMembership)
                .Where(u => u.Id == userId)
                .FirstOrDefaultAsync();

            UserPermissions userPermissions = _mapper.Map<UserPermissions>(userDetails);

            // UserPermissions userPermissions = await _context.Users
            //     .Where(u => u.Id == userId)
            //     .ProjectTo<UserPermissions>(_mapper.ConfigurationProvider)
            //     .FirstOrDefaultAsync();

            if (userPermissions.Permissions.Where(x => x.Key == PlayerClaimTypes.SystemAdmin.ToString()).Any())
            {
                claims.Add(new Claim(ClaimTypes.Role, PlayerClaimTypes.SystemAdmin.ToString()));
            }

            foreach (var teamPermission in userPermissions.TeamPermissions)
            {
                if (!claims.Where(c => c.Type == PlayerClaimTypes.ViewMember.ToString() && c.Value == teamPermission.ViewId.ToString()).Any())
                {
                    claims.Add(new Claim(PlayerClaimTypes.ViewMember.ToString(), teamPermission.ViewId.ToString()));
                }

                claims.Add(new Claim(PlayerClaimTypes.TeamMember.ToString(), teamPermission.TeamId.ToString()));

                if (teamPermission.IsPrimary)
                {
                    claims.Add(new Claim(PlayerClaimTypes.PrimaryTeam.ToString(), teamPermission.TeamId.ToString()));

                    if (teamPermission.Permissions.Where(x => x.Key == PlayerClaimTypes.ViewAdmin.ToString()).Any())
                    {
                        claims.Add(new Claim(PlayerClaimTypes.ViewAdmin.ToString(), teamPermission.ViewId.ToString()));
                    }
                }
            }

            return claims;
        }

        private void addNewClaims(ClaimsIdentity identity, List<Claim> claims)
        {
            var newClaims = new List<Claim>();
            claims.ForEach(delegate (Claim claim)
            {
                if (!identity.Claims.Any(identityClaim => identityClaim.Type == claim.Type))
                {
                    newClaims.Add(claim);
                }
            });
            identity.AddClaims(newClaims);
        }
    }
}
