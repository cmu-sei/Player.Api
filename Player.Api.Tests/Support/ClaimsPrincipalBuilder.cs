// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;

namespace Player.Api.Tests.Support;

/// <summary>
/// Builds the <see cref="ClaimsPrincipal"/> that <c>IIdentityResolver</c> would hand the
/// authorization stack for a signed-in user.
/// </summary>
/// <remarks>
/// Every authorization decision in this application is a function of these claims, so almost every
/// permission test starts here. The claim shapes mirror what
/// <c>Player.Api.Infrastructure.ClaimsTransformation</c> produces at request time: a <c>sub</c>
/// claim, one <c>Permission</c> claim per system permission, and one <c>TeamPermission</c> claim
/// per team the user has a resolved claim for, whose value is a serialized
/// <see cref="TeamPermissionsClaim"/>.
/// </remarks>
public sealed class ClaimsPrincipalBuilder
{
    private const string AuthenticationType = "Test";

    private readonly List<Claim> _claims = [];
    private Guid _userId = Guid.NewGuid();
    private string _name = "Test User";

    /// <summary>
    /// The id that <c>ClaimsPrincipalExtensions.GetId</c> will read back, which is what
    /// <c>IPlayerAuthorizationService.IsCurrentUser</c> compares against.
    /// </summary>
    public Guid UserId => _userId;

    /// <summary>The value of the <c>name</c> claim.</summary>
    public string Name => _name;

    public ClaimsPrincipalBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>
    /// Sets the <c>name</c> claim, which is always present because <c>NotificationService</c> reads it
    /// with <c>Claims.Single</c> — without one, every notification path throws
    /// <see cref="InvalidOperationException"/> instead of running.
    /// </summary>
    public ClaimsPrincipalBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// Adds system permissions, which grant across every view and team.
    /// </summary>
    public ClaimsPrincipalBuilder WithSystemPermissions(params SystemPermission[] permissions)
    {
        foreach (var permission in permissions)
        {
            _claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, permission.ToString()));
        }

        return this;
    }

    /// <summary>
    /// Adds a raw system-permission claim value. Use this for values that are not valid
    /// <see cref="SystemPermission"/> names, to cover permissions removed from the enum while
    /// still present in a role in the database.
    /// </summary>
    public ClaimsPrincipalBuilder WithRawSystemPermission(string value)
    {
        _claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, value));
        return this;
    }

    /// <summary>
    /// Adds a team claim built from strongly typed permissions. <paramref name="viewPermissions"/>
    /// and <paramref name="teamPermissions"/> populate both the effective and direct permission
    /// lists, which is the common case — a permission held directly is also effective.
    /// </summary>
    public ClaimsPrincipalBuilder WithTeam(
        Guid viewId,
        Guid teamId,
        bool isPrimary = false,
        ViewPermission[] viewPermissions = null,
        TeamPermission[] teamPermissions = null)
    {
        var values = Values(viewPermissions, teamPermissions);

        return WithTeamClaim(new TeamPermissionsClaim
        {
            ViewId = viewId,
            TeamId = teamId,
            IsPrimary = isPrimary,
            PermissionValues = values,
            DirectPermissionValues = values
        });
    }

    /// <summary>
    /// Adds a team claim whose permissions were granted by a scoped team permission rather than
    /// held directly: effective permissions differ from direct ones, and
    /// <paramref name="sourceTeamIds"/> names the teams the grant came from.
    /// </summary>
    /// <remarks>
    /// This is the shape <c>GetPrimaryVisibilityContext</c> and <c>GetVisibleTeamIds</c> exist to
    /// interpret, and the distinction the scoped-team-permissions feature turns on — so tests must
    /// be able to produce a claim where the two lists disagree.
    /// </remarks>
    public ClaimsPrincipalBuilder WithScopedTeam(
        Guid viewId,
        Guid teamId,
        Guid[] sourceTeamIds,
        bool isPrimary = false,
        ViewPermission[] viewPermissions = null,
        TeamPermission[] teamPermissions = null,
        ViewPermission[] directViewPermissions = null,
        TeamPermission[] directTeamPermissions = null)
    {
        return WithTeamClaim(new TeamPermissionsClaim
        {
            ViewId = viewId,
            TeamId = teamId,
            IsPrimary = isPrimary,
            PermissionValues = Values(viewPermissions, teamPermissions),
            DirectPermissionValues = Values(directViewPermissions, directTeamPermissions),
            SourceTeamIds = sourceTeamIds ?? []
        });
    }

    /// <summary>
    /// Adds a fully specified team claim, for cases the typed overloads cannot express — unparseable
    /// permission values, or a null <c>SourceTeamIds</c> array.
    /// </summary>
    public ClaimsPrincipalBuilder WithTeamClaim(TeamPermissionsClaim claim)
    {
        _claims.Add(new Claim(AuthorizationConstants.TeamPermissionsClaimType, claim.ToString()));
        return this;
    }

    /// <summary>
    /// Adds an arbitrary claim, for asserting that unrelated claim types are ignored.
    /// </summary>
    public ClaimsPrincipalBuilder WithClaim(string type, string value)
    {
        _claims.Add(new Claim(type, value));
        return this;
    }

    /// <summary>
    /// Adds a claim with an explicit value type. <c>UserClaimsService</c>'s roles-from-token reader
    /// dispatches on it, so a claim carrying JSON has to say so rather than defaulting to string.
    /// </summary>
    public ClaimsPrincipalBuilder WithClaim(string type, string value, string valueType)
    {
        _claims.Add(new Claim(type, value, valueType));
        return this;
    }

    public ClaimsPrincipal Build()
    {
        var claims = new List<Claim>(_claims)
        {
            new("sub", _userId.ToString()),
            new("name", _name)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }

    /// <summary>
    /// An authenticated principal carrying no permissions at all — the baseline that every
    /// authorization check must reject.
    /// </summary>
    public static ClaimsPrincipal Anonymous() => new ClaimsPrincipalBuilder().Build();

    private static string[] Values(ViewPermission[] viewPermissions, TeamPermission[] teamPermissions) =>
    [
        .. (viewPermissions ?? []).Select(x => x.ToString()),
        .. (teamPermissions ?? []).Select(x => x.ToString())
    ];
}
