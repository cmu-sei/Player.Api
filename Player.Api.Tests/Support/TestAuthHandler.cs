// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Player.Api.Tests.Support;

/// <summary>
/// Stands in for the identity provider, minting the identity a validated token would have produced.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the test host deviates from production, and it replaces only token
/// validation. Everything downstream is real: <c>AuthorizationClaimsTransformer</c> runs on the
/// identity this mints and derives every permission claim from the database, so what an actor may do
/// is decided by the rows a test seeds.
/// </para>
/// <para>
/// The <c>scope</c> claims come from <c>Authorization:AuthorizationScope</c> because
/// <c>Startup.ApplyPolicies</c> puts a <c>RequireClaim("scope", x)</c> in the default policy for each
/// space-separated entry, and <c>RequireAuthorization()</c> applies that policy to the whole
/// <c>/api/</c> group. Without them every request would be a 403.
/// </para>
/// </remarks>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The scheme's name. Not <c>Scheme</c>, which the base class already uses for the registration
    /// this handler was resolved for.
    /// </summary>
    public const string SchemeName = "Test";

    /// <summary>The user id, which becomes the <c>sub</c> claim. Absent means unauthenticated.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>
    /// The <c>name</c> claim. <c>UserClaimsService.ValidateUser</c> writes it to the user row, and
    /// <c>NotificationService</c> reads it with <c>Claims.Single</c>.
    /// </summary>
    public const string NameHeader = "X-Test-Name";

    private readonly string[] _scopes;

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _scopes = (configuration["Authorization:AuthorizationScope"] ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
        {
            // NoResult rather than Fail, so the pipeline challenges and the response is a 401. This is
            // what makes an unauthenticated request testable.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(user.ToString(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"{UserHeader} is not a Guid: '{user}'."));
        }

        List<Claim> claims = [new("sub", userId.ToString())];

        if (Request.Headers.TryGetValue(NameHeader, out var name))
        {
            claims.Add(new Claim("name", name.ToString()));
        }

        claims.AddRange(_scopes.Select(scope => new Claim("scope", scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
