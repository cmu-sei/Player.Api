// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Extensions;

namespace Player.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// The two pieces of token handling every request depends on: which user a token names, and how its
/// scopes are read.
/// </summary>
public class ClaimsPrincipalExtensionsTests
{
    private const string NameIdentifier =
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

    [Fact]
    public void GetId_reads_the_sub_claim()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, Principal(new Claim("sub", id.ToString())).GetId());
    }

    /// <summary>
    /// Identity providers that emit the WS-Federation name identifier instead of <c>sub</c> still
    /// resolve, which is what lets one deployment sit behind either.
    /// </summary>
    [Fact]
    public void GetId_falls_back_to_the_name_identifier_claim()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, Principal(new Claim(NameIdentifier, id.ToString())).GetId());
    }

    [Fact]
    public void GetId_prefers_sub_when_both_are_present()
    {
        var sub = Guid.NewGuid();

        var principal = Principal(
            new Claim("sub", sub.ToString()),
            new Claim(NameIdentifier, Guid.NewGuid().ToString()));

        Assert.Equal(sub, principal.GetId());
    }

    /// <summary>
    /// A null principal is the unauthenticated case, and reads as the empty id rather than throwing.
    /// </summary>
    [Fact]
    public void GetId_returns_the_empty_id_for_no_principal()
    {
        Assert.Equal(Guid.Empty, ((ClaimsPrincipal)null).GetId());
    }

    /// <summary>
    /// Characterizes current behaviour. A principal carrying neither claim throws rather than reading as
    /// empty: the fallback parse is handed the same null the first one failed on.
    /// </summary>
    [Fact]
    public void GetId_throws_for_a_principal_with_neither_claim()
    {
        Assert.Throws<ArgumentNullException>(() => Principal().GetId());
    }

    [Fact]
    public void GetId_throws_for_a_subject_that_is_not_a_guid()
    {
        Assert.Throws<ArgumentNullException>(() => Principal(new Claim("sub", "not-a-guid")).GetId());
    }

    /// <summary>
    /// A space-delimited <c>scope</c> claim is how most providers emit several scopes, and policy checks
    /// match one claim at a time — so it is split into one claim each.
    /// </summary>
    [Fact]
    public void NormalizeScopeClaims_splits_a_space_delimited_scope_claim()
    {
        var normalized = Principal(new Claim("scope", "player-api openid  profile"))
            .NormalizeScopeClaims();

        Assert.Equal(
            ["player-api", "openid", "profile"],
            normalized.FindAll("scope").Select(x => x.Value));
    }

    [Fact]
    public void NormalizeScopeClaims_leaves_a_single_scope_claim_alone()
    {
        var normalized = Principal(new Claim("scope", "player-api")).NormalizeScopeClaims();

        Assert.Equal("player-api", Assert.Single(normalized.FindAll("scope")).Value);
    }

    [Fact]
    public void NormalizeScopeClaims_keeps_the_other_claims()
    {
        var id = Guid.NewGuid();

        var normalized = Principal(
            new Claim("sub", id.ToString()),
            new Claim("scope", "a b")).NormalizeScopeClaims();

        Assert.Equal(id, normalized.GetId());
    }

    /// <summary>
    /// The split claims carry the original's issuer and value type, so a policy that filters on either
    /// still matches.
    /// </summary>
    [Fact]
    public void NormalizeScopeClaims_preserves_the_issuer_of_a_split_claim()
    {
        var original = new Claim("scope", "a b", ClaimValueTypes.String, "https://identity.test");

        var normalized = Principal(original).NormalizeScopeClaims();

        Assert.All(normalized.FindAll("scope"), claim =>
        {
            Assert.Equal("https://identity.test", claim.Issuer);
            Assert.Equal(ClaimValueTypes.String, claim.ValueType);
        });
    }

    /// <summary>
    /// The identity's own configuration survives, so the rebuilt principal still knows which claim types
    /// carry its name and roles.
    /// </summary>
    [Fact]
    public void NormalizeScopeClaims_preserves_the_identity_configuration()
    {
        var identity = new ClaimsIdentity(
            [new Claim("scope", "a b"), new Claim("username", "tester")],
            authenticationType: "Bearer",
            nameType: "username",
            roleType: "roles");

        var normalized = new ClaimsPrincipal(identity).NormalizeScopeClaims();

        var rebuilt = Assert.Single(normalized.Identities);
        Assert.Equal("Bearer", rebuilt.AuthenticationType);
        Assert.Equal("tester", rebuilt.Name);
        Assert.Equal(2, normalized.FindAll("scope").Count());
    }

    [Fact]
    public void NormalizeScopeClaims_handles_a_principal_with_several_identities()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity([new Claim("scope", "a b")]),
            new ClaimsIdentity([new Claim("scope", "c")])
        ]);

        var normalized = principal.NormalizeScopeClaims();

        Assert.Equal(2, normalized.Identities.Count());
        Assert.Equal(["a", "b", "c"], normalized.FindAll("scope").Select(x => x.Value));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims));
}
