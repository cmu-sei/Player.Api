// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// Covers <see cref="ViewMemberHandler"/>, which <c>Startup.cs:400</c> registers but nothing uses: no policy
/// carries a <see cref="ViewMemberRequirement"/> and nothing issues the claim it reads. These tests pin what
/// it would do if it were reached, so reviving it — or removing it — is a decision made against known
/// behavior rather than a guess.
/// </summary>
public class ViewMemberHandlerTests
{
    private readonly ViewMemberHandler _handler = new();
    private static readonly Guid ViewId = Guid.NewGuid();

    /// <summary>
    /// The claim is matched by exact string, so the two representations of a Guid do not interoperate —
    /// whoever starts issuing this claim has to use "D" format, which is what <c>ToString()</c> gives.
    /// </summary>
    [Fact]
    public async Task Succeeds_when_the_user_holds_the_view_member_claim_for_that_view()
    {
        var context = await Handle(UserWith(ViewId.ToString()));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_for_a_claim_naming_a_different_view()
    {
        var context = await Handle(UserWith(Guid.NewGuid().ToString()));

        Assert.False(context.HasSucceeded);

        // Falls through rather than failing, so another handler can still grant the same requirement.
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Does_not_succeed_when_the_user_holds_no_view_member_claim()
    {
        var context = await Handle(ClaimsPrincipalBuilder.Anonymous());

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    /// <summary>Brace format is a real risk here: it is what <c>Guid.ToString("B")</c> produces.</summary>
    [Fact]
    public async Task Does_not_succeed_for_the_same_view_id_in_a_different_format()
    {
        var context = await Handle(UserWith(ViewId.ToString("B")));

        Assert.False(context.HasSucceeded);
    }

    private Task<Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext> Handle(
        ClaimsPrincipal user) =>
        AuthorizationHarness.HandleAsync(_handler, new ViewMemberRequirement(ViewId), user);

    private static ClaimsPrincipal UserWith(string viewId) =>
        new ClaimsPrincipalBuilder()
            .WithClaim(PlayerClaimTypes.ViewMember.ToString(), viewId)
            .Build();
}
