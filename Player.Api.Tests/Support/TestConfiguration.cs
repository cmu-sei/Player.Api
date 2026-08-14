// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Player.Api.Tests.Support;

/// <summary>
/// The configuration <see cref="PlayerAppFactory"/> layers over the application's own
/// <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>WebApplicationFactory</c> resolves the content root to the <c>Player.Api</c> project directory,
/// so the shipped configuration is already in force and only keys whose shipped value breaks or
/// weakens a test run belong here. Every entry below states which.
/// </para>
/// <para>
/// Override whole coherent groups or none of a group. Setting <c>CorsPolicy:AllowAnyOrigin</c> on its
/// own left the file's <c>SupportsCredentials</c> in place, a combination
/// <c>CorsPolicyBuilder.Build()</c> rejects outright, and the host failed to start.
/// </para>
/// </remarks>
internal static class TestConfiguration
{
    /// <summary>
    /// Where <c>FileService</c> writes. Created by the application on first upload and removed when
    /// the factory is disposed.
    /// </summary>
    public static readonly string FileUploadBasePath =
        Path.Combine(Path.GetTempPath(), $"player-tests-{Guid.NewGuid():N}");

    public static Dictionary<string, string> Values => new()
    {
        // Startup gates its two IHostedService registrations on this, and Program.Main gates
        // InitializeDatabase on the same switch reaching it as a command-line argument — see
        // PlayerAppFactory. Both services have their own tests, which drive them directly.
        ["open-api-only"] = "true",

        // Must match a case of Startup's provider switch. The second switch, which registers the
        // health check, has no default either, so an unmatched provider leaves AddHealthChecks
        // uncalled and MapHealthChecks throws while the host is still building. Sqlite is the
        // cheapest match: the check opens its connection only when /api/health/* is requested, and
        // the context registration the first switch makes is replaced per request anyway. Pinned
        // rather than inherited so the harness does not break if the application's default changes.
        ["Database:Provider"] = "Sqlite",

        // The shipped value names a file, which the health check would create in the content root —
        // the Player.Api project directory under test.
        ["ConnectionStrings:Sqlite"] = "Data Source=:memory:",

        // One host serves the whole run, and the claims cache is keyed on user id alone. Cached
        // claims would leak across tests: a user whose permissions one test seeds would keep them in
        // the next test that happens to use the same id. AuthCacheEvictionTests drives the cache
        // directly, where caching is the subject rather than a shared surface.
        ["ClaimsTransformation:EnableCaching"] = "false",
        ["ClaimsTransformation:CacheExpirationSeconds"] = "60",
        // Permissions come from the rows a test seeds, so that one mechanism decides what an actor
        // may do. UserClaimsServiceTests covers reading roles from the token.
        ["ClaimsTransformation:UseRolesFromIdP"] = "false",
        ["ClaimsTransformation:RolesClaimPath"] = "realm_access.roles",

        // The shipped value is relative, so uploads would land in the Player.Api project directory.
        ["FileUpload:basePath"] = FileUploadBasePath
    };
}
