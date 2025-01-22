// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Options;

namespace Player.Api.Services;

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
        ClaimsCacheEntry claimsCacheEntry;
        var identity = (ClaimsIdentity)principal.Identity;
        var userId = principal.GetId();

        // Don't use cached claims if given a new token and we are using roles from the token
        if (_cache.TryGetValue(userId, out claimsCacheEntry) && _options.UseRolesFromIdP)
        {
            var cachedTokenId = claimsCacheEntry.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Jti)?.Value;
            var newTokenId = identity.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Jti)?.Value;

            if (newTokenId != cachedTokenId)
            {
                var tokenRoleNames = this.GetClaimsFromToken(principal, _options.RolesClaimPath).Select(x => x.ToLower());

                if (!new HashSet<string>(tokenRoleNames).SetEquals(claimsCacheEntry.TokenRoleNames))
                {
                    claimsCacheEntry = null;
                }
            }
        }

        if (claimsCacheEntry == null)
        {
            List<Claim> claims = [];
            var user = await ValidateUser(userId, principal.FindFirst("name")?.Value, update);

            if (user != null)
            {
                var jtiClaim = identity.Claims.Where(x => x.Type == JwtRegisteredClaimNames.Jti).FirstOrDefault();

                if (jtiClaim is not null)
                {
                    claims.Add(new Claim(jtiClaim.Type, jtiClaim.Value));
                }

                claims.AddRange(await GetUserClaims(userId));
                claimsCacheEntry = await GetPermissionClaims(userId, principal);
                claimsCacheEntry.Claims = claims.Concat(claimsCacheEntry.Claims);

                if (_options.EnableCaching)
                {
                    _cache.Set(userId, claimsCacheEntry, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheExpirationSeconds)));
                }
            }
        }

        addNewClaims(identity, claimsCacheEntry.Claims);
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
        var anyUsers = await _context.Users.AnyAsync();
        var user = await _context.Users
            .Where(u => u.Id == subClaim)
            .SingleOrDefaultAsync();

        if (update)
        {
            if (user == null)
            {
                user = new UserEntity
                {
                    Id = subClaim,
                    Name = nameClaim ?? "Anonymous"
                };

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

    private async Task<ClaimsCacheEntry> GetPermissionClaims(Guid userId, ClaimsPrincipal principal)
    {
        List<Claim> claims = new();

        var tokenRoleNames = _options.UseRolesFromIdP ?
            this.GetClaimsFromToken(principal, _options.RolesClaimPath).Select(x => x.ToLower()) :
            [];

        var roles = await _context.Roles
            .Where(x => tokenRoleNames.Contains(x.Name.ToLower()))
            .ToListAsync();

        var userRole = await _context.Users
            .Where(x => x.Id == userId)
            .Include(x => x.Role)
                .ThenInclude(x => x.Permissions)
                    .ThenInclude(x => x.Permission)
            .Select(x => x.Role)
            .FirstOrDefaultAsync();

        if (userRole != null)
        {
            roles.Add(userRole);
        }

        roles = roles.Distinct().ToList();

        foreach (var role in roles)
        {
            List<string> permissions;

            if (role.AllPermissions)
            {
                permissions = Enum.GetValues<SystemPermission>().Select(x => x.ToString()).ToList();
            }
            else
            {
                permissions = role.Permissions.Select(x => x.Permission.Name).ToList();
            }

            foreach (var permission in permissions)
            {
                if (!claims.Any(x => x.Type == AuthorizationConstants.PermissionsClaimType &&
                    x.Value == permission))
                {
                    claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, permission));
                }
                ;
            }
        }

        // Get Team Permissions
        var teamMemberships = await _context.TeamMemberships
            .Where(x => x.UserId == userId)
            .Include(x => x.ViewMembership)
            .Include(x => x.Role)
                .ThenInclude(x => x.Permissions)
                    .ThenInclude(x => x.Permission)
            .Include(x => x.Team)
                .ThenInclude(x => x.Role)
                    .ThenInclude(x => x.Permissions)
                        .ThenInclude(x => x.Permission)
            .Include(x => x.Team)
                .ThenInclude(x => x.Permissions)
                    .ThenInclude(x => x.Permission)
            .ToListAsync();

        var allTeamPermissionValues = await _context.TeamPermissions.Select(x => x.Name).ToArrayAsync();

        foreach (var membership in teamMemberships)
        {
            var teamPermissions = new List<string>();

            if ((membership?.Role?.AllPermissions ?? false) || (membership?.Team?.Role?.AllPermissions ?? false))
            {
                teamPermissions.AddRange(allTeamPermissionValues);
            }
            else
            {
                teamPermissions.AddRange(membership.Team.Permissions.Select(x => x.Permission.Name));
                teamPermissions.AddRange(membership.Team.Role?.Permissions.Select(x => x.Permission.Name) ?? []);
                teamPermissions.AddRange(membership.Role?.Permissions.Select(x => x.Permission.Name) ?? []);
            }

            var permissionsClaim = new TeamPermissionsClaim
            {
                TeamId = membership.TeamId,
                ViewId = membership.Team.ViewId,
                PermissionValues = teamPermissions.ToArray(),
                IsPrimary = membership.ViewMembership.PrimaryTeamMembershipId == membership.Id
            };

            claims.Add(new Claim(AuthorizationConstants.TeamPermissionsClaimType, permissionsClaim.ToString()));
        }

        return new ClaimsCacheEntry
        {
            Claims = claims,
            TokenRoleNames = tokenRoleNames,
        };
    }

    private string[] GetClaimsFromToken(ClaimsPrincipal principal, string claimPath)
    {
        if (string.IsNullOrEmpty(claimPath))
        {
            return [];
        }

        // Name of the claim to insert into the token. This can be a fully qualified name like 'address.street'.
        // In this case, a nested json object will be created. To prevent nesting and use dot literally, escape the dot with backslash (\.).
        var pathSegments = Regex.Split(claimPath, @"(?<!\\)\.").Select(s => s.Replace("\\.", ".")).ToArray();

        var tokenClaim = principal.Claims.Where(x => x.Type == pathSegments.First()).FirstOrDefault();

        if (tokenClaim == null)
        {
            return [];
        }

        return tokenClaim.ValueType switch
        {
            ClaimValueTypes.String => [tokenClaim.Value],
            JsonClaimValueTypes.Json => ExtractJsonClaimValues(tokenClaim.Value, pathSegments.Skip(1)),
            _ => []
        };
    }

    private string[] ExtractJsonClaimValues(string json, IEnumerable<string> pathSegments)
    {
        List<string> values = new();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement currentElement = doc.RootElement;

            foreach (var segment in pathSegments)
            {
                if (!currentElement.TryGetProperty(segment, out JsonElement propertyElement))
                {
                    return [];
                }

                currentElement = propertyElement;
            }

            if (currentElement.ValueKind == JsonValueKind.Array)
            {
                values.AddRange(currentElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()));
            }
            else if (currentElement.ValueKind == JsonValueKind.String)
            {
                values.Add(currentElement.GetString());
            }
        }
        catch (JsonException)
        {
            // Handle invalid JSON format
        }

        return values.ToArray();
    }

    private async Task<IEnumerable<Claim>> GetUserClaims(Guid userId)
    {
        List<Claim> claims = new List<Claim>();

        var teamMemberships = await _context.TeamMemberships
            .Include(x => x.ViewMembership)
            .Where(x => x.UserId == userId)
            .ToArrayAsync();

        foreach (var teamMembership in teamMemberships)
        {
            if (!claims.Where(c => c.Type == PlayerClaimTypes.ViewMember.ToString() && c.Value == teamMembership.ViewMembership.ViewId.ToString()).Any())
            {
                claims.Add(new Claim(PlayerClaimTypes.ViewMember.ToString(), teamMembership.ViewMembership.ViewId.ToString()));
            }

            claims.Add(new Claim(PlayerClaimTypes.TeamMember.ToString(), teamMembership.TeamId.ToString()));

            if (teamMembership.Id == teamMembership.ViewMembership.PrimaryTeamMembershipId)
            {
                claims.Add(new Claim(PlayerClaimTypes.PrimaryTeam.ToString(), teamMembership.TeamId.ToString()));
            }
        }

        return claims;
    }

    private void addNewClaims(ClaimsIdentity identity, IEnumerable<Claim> claims)
    {
        var newClaims = new List<Claim>();
        foreach (var claim in claims)
        {
            if (!identity.Claims.Any(identityClaim => identityClaim.Type == claim.Type))
            {
                newClaims.Add(claim);
            }
        }
        identity.AddClaims(newClaims);
    }

    private class ClaimsCacheEntry
    {
        public IEnumerable<Claim> Claims { get; set; }
        public IEnumerable<string> TokenRoleNames { get; set; }
    }
}
