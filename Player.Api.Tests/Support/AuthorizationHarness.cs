// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Data.Data;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;

namespace Player.Api.Tests.Support;

/// <summary>
/// Wires up the authorization stack the way <c>Startup.ApplyPolicies</c> does, so tests exercise
/// the real ASP.NET Core <see cref="IAuthorizationService"/> and the real handlers.
/// </summary>
/// <remarks>
/// Substituting <see cref="IAuthorizationService"/> would make
/// <c>Player.Api.Infrastructure.Authorization.AuthorizationService</c> tests assert only that it
/// asked the right question, never that it got the right answer — and the interesting behavior
/// (which requirement wins, when a handler falls through) lives in the handlers.
/// </remarks>
public static class AuthorizationHarness
{
    /// <summary>
    /// The framework authorization service with both production permission handlers registered.
    /// </summary>
    public static IAuthorizationService CreateFrameworkAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, SystemPermissionsHandler>();
        services.AddSingleton<IAuthorizationHandler, TeamPermissionsHandler>();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// An identity resolver over a fixed principal, standing in for the one backed by
    /// <c>IHttpContextAccessor</c> at request time.
    /// </summary>
    public static IIdentityResolver IdentityResolverFor(ClaimsPrincipal principal)
    {
        var resolver = Substitute.For<IIdentityResolver>();
        resolver.GetClaimsPrincipal().Returns(principal);
        resolver.GetId().Returns(principal.GetId());

        return resolver;
    }

    /// <summary>
    /// The production <see cref="IPlayerAuthorizationService"/> with real collaborators, acting as
    /// <paramref name="principal"/>.
    /// </summary>
    public static IPlayerAuthorizationService CreatePlayerAuthorizationService(
        ClaimsPrincipal principal,
        PlayerContext dbContext) =>
        new AuthorizationService(
            CreateFrameworkAuthorizationService(),
            IdentityResolverFor(principal),
            dbContext);

    /// <summary>
    /// Runs a requirement through a handler directly, returning the resulting context so a test can
    /// assert on <see cref="AuthorizationHandlerContext.HasSucceeded"/> and
    /// <see cref="AuthorizationHandlerContext.HasFailed"/> separately — the two are not opposites.
    /// </summary>
    public static async Task<AuthorizationHandlerContext> HandleAsync<TRequirement>(
        IAuthorizationHandler handler,
        TRequirement requirement,
        ClaimsPrincipal user)
        where TRequirement : IAuthorizationRequirement
    {
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await handler.HandleAsync(context);

        return context;
    }
}
