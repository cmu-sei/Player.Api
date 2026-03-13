# Player.Api.Tests.Integration

Integration tests for Player API using WebApplicationFactory and Testcontainers PostgreSQL.

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

## Purpose

This project tests Player API endpoints end-to-end using a real web server and PostgreSQL database running in Docker containers via Testcontainers. Tests verify HTTP behavior, routing, middleware, authentication, and database integration without external OIDC dependencies.

## Files

### Fixtures/PlayerTestContext.cs

`WebApplicationFactory<Program>` backed by a Testcontainers PostgreSQL instance. Replaces production configuration with test-specific settings:

**Database:**
- Spins up `postgres:16-alpine` container via Testcontainers
- Database: `player_test`, User: `test`, Password: `test`
- Replaces production DbContext registration with test container connection string
- Runs `db.Database.EnsureCreated()` to apply migrations on startup

**Authentication:**
- Removes OIDC authentication
- Registers `TestAuthHandler` custom authentication scheme
- Bypasses real Identity Server for test isolation

**Configuration Overrides:**
```csharp
["Database:Provider"] = "PostgreSQL",
["Database:AutoMigrate"] = "false",
["ConnectionStrings:PostgreSQL"] = _postgres.GetConnectionString(),
["Authorization:Authority"] = "https://localhost",
["open-api-only"] = "true"  // Disables background services
```

Implements `IAsyncLifetime` for xUnit test collection lifecycle:
- `InitializeAsync()` - Starts PostgreSQL container
- `DisposeAsync()` - Stops and removes container

### Fixtures/TestAuthHandler.cs

Custom `AuthenticationHandler<AuthenticationSchemeOptions>` that always succeeds with a test user identity. Bypasses real OIDC authentication for integration tests.

**Test User:**
- `TestUserId`: `9fd3c38e-58b0-4af1-80d1-1895af91f1f9` (fixed GUID)
- `TestUserName`: `"Test User"`
- Scope: `player-api`

**Claims Provided:**
```csharp
new Claim("sub", TestUserId.ToString()),
new Claim(ClaimTypes.Name, TestUserName),
new Claim("scope", "player-api")
```

All requests to the test server automatically authenticate with this identity, allowing tests to focus on authorization logic rather than authentication mechanics.

### Tests/Controllers/HealthCheckTests.cs

Validates health check endpoints:

- `Health_Live_ReturnsOk` - Tests `/api/health/live` returns 200 OK
- `Health_Ready_ReturnsOk` - Tests `/api/health/ready` returns 200 OK

Simple smoke tests verifying the API starts and responds to basic requests.

### Tests/Controllers/FileControllerTests.cs

Tests File API endpoints with authorization and error handling:

- `GetAllFiles_Unauthorized_ReturnsForbiddenOrUnauthorized` - Verifies permission enforcement (TestAuthHandler authenticates but user has no system permissions; should not return 200)
- `GetFileById_NonExistentFile_ReturnsNotFound` - Tests 404 handling for missing files (or 403 if permission check runs first)
- `DeleteFile_NonExistentFile_ReturnsNotFoundOrForbidden` - Tests DELETE validation
- `GetViewFiles_NonExistentView_ReturnsNotFoundOrForbidden` - Tests view-scoped file retrieval

These tests demonstrate that:
1. Authentication works (TestAuthHandler provides identity)
2. Authorization correctly blocks unauthorized operations
3. Middleware translates service exceptions to HTTP status codes
4. Permission checks may run before entity existence checks (hence 403 OR 404 assertions)

## Running Tests

From the `player.api` directory:

```bash
# Run all integration tests
dotnet test Player.Api.Tests.Integration/Player.Api.Tests.Integration.csproj

# Run specific test class
dotnet test Player.Api.Tests.Integration/Player.Api.Tests.Integration.csproj --filter "FullyQualifiedName~HealthCheckTests"

# Run specific test method
dotnet test Player.Api.Tests.Integration/Player.Api.Tests.Integration.csproj --filter "FullyQualifiedName~FileControllerTests.GetAllFiles_Unauthorized_ReturnsForbiddenOrUnauthorized"

# Run with verbose output
dotnet test Player.Api.Tests.Integration/Player.Api.Tests.Integration.csproj --logger "console;verbosity=detailed"
```

## Prerequisites

**Docker** must be running. Testcontainers requires Docker to spin up PostgreSQL containers.

Linux/WSL2:
```bash
sudo service docker start
```

macOS/Windows:
```bash
# Ensure Docker Desktop is running
```

## Key Patterns

### Test Class Setup

All integration test classes use `IClassFixture<PlayerTestContext>` to share a single web server and database across all tests in the class:

```csharp
public class FileControllerTests : IClassFixture<PlayerTestContext>
{
    private readonly HttpClient _client;

    public FileControllerTests(PlayerTestContext factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAllFiles_Unauthorized_ReturnsForbiddenOrUnauthorized()
    {
        var response = await _client.GetAsync("/api/files");
        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }
}
```

### HTTP Client

`factory.CreateClient()` returns an `HttpClient` configured to send requests to the in-memory test server. No actual network calls are made; requests go through the full ASP.NET Core pipeline.

### Assertion Library

Tests use **Shouldly** for fluent assertions:

```csharp
response.StatusCode.ShouldBe(HttpStatusCode.OK);
(status == HttpStatusCode.NotFound || status == HttpStatusCode.Forbidden)
    .ShouldBeTrue($"Expected 404 or 403 but got {status}");
```

### Authentication

All requests automatically include the test user identity from `TestAuthHandler`. To test different authorization scenarios, seed the database with appropriate permissions for the test user ID (`9fd3c38e-58b0-4af1-80d1-1895af91f1f9`).

## Test Isolation

Each test class gets a fresh database via `db.Database.EnsureCreated()`. However, tests within a class **share the same database instance** for performance. If test isolation is needed, either:

1. Use separate test classes (each gets its own container)
2. Manually reset database state in test setup
3. Use transactions and rollback (requires additional setup)

## Dependencies

- Microsoft.AspNetCore.Mvc.Testing 10.0.1
- Testcontainers.PostgreSql 4.0.0
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
- AutoFixture 4.18.1 + AutoFakeItEasy
- FakeItEasy 8.3.0
- Shouldly 4.2.1
- xUnit 2.9.3
- Player.Api
- Player.Api.Data
- Player.Api.Tests.Shared
- Crucible.Common.Testing

## Performance

Integration tests are slower than unit tests due to:
- Docker container startup (PostgreSQL)
- Full ASP.NET Core pipeline
- Real database operations

Expect ~2-5 seconds overhead per test class (container startup), then milliseconds per test.

## Coverage Target

Integration tests focus on:
- HTTP routing and middleware behavior
- End-to-end authentication/authorization flow
- Database integration
- Error handling and status code translation

Combined with unit tests, target 80%+ overall code coverage.
