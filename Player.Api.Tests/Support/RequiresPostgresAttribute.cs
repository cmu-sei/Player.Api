// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.CompilerServices;

namespace Player.Api.Tests.Support;

/// <summary>
/// A test that only makes sense on PostgreSQL — snake_case casing, store-generated UUIDs, real
/// migration history, delete-graph ordering. Skips on the SQLite fallback.
/// </summary>
/// <remarks>
/// Uses xUnit v3's static skip predicate. CI sets
/// <see cref="DatabaseFixture.RequirePostgresVariable"/>, so these never silently skip there.
/// </remarks>
public sealed class RequiresPostgresAttribute : FactAttribute
{
    /// <remarks>
    /// The caller-info parameters are what let a skipped or failed test report its own source
    /// location instead of this file's (xUnit3003).
    /// </remarks>
    public RequiresPostgresAttribute(
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Requires PostgreSQL; the SQLite fallback is active, either because no usable Docker "
            + $"daemon was found or because {DatabaseFixture.ForceSqliteVariable} is set.";
        SkipUnless = nameof(DatabaseFixture.PostgresActive);
        SkipType = typeof(DatabaseFixture);
    }
}
