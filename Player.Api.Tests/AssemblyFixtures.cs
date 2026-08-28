// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Tests.Support;

// Starting a PostgreSQL container and running 34 migrations costs seconds, so it happens once for
// the whole assembly. xUnit v3 constructs this before the first test, awaits its InitializeAsync,
// disposes it after the last test, and injects it into any test class with a matching constructor
// parameter — see DatabaseTestBase.
[assembly: AssemblyFixture(typeof(DatabaseFixture))]

// Starting the application costs about a second, and everything it registers as a singleton is shared
// by every test that uses it — see PlayerAppFactory, which says how each shared surface is dealt with.
[assembly: AssemblyFixture(typeof(PlayerAppFactory))]
