# Player.Api.Tests.Unit

Unit tests for Player API services and AutoMapper configuration.

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

## Purpose

This project contains isolated unit tests for Player API business logic, focusing on service layer methods and AutoMapper profile validation. Player uses a mixed controllers + MediatR Features pattern for request handling.

## Files

### MappingConfigurationTests.cs

Validates AutoMapper configuration for all mapping profiles in the Player.Api assembly. This test is more complex than typical mapping tests because Player uses custom value resolvers that require service dependencies:

- `TeamMemberResolver` - requires `IAuthorizationService` and `IUserClaimsService`
- `PrimaryTeamResolver` - requires `IAuthorizationService` and `IUserClaimsService`

The test uses `ConstructServicesUsing` to provide fake implementations of these dependencies:

```csharp
cfg.ConstructServicesUsing(type =>
{
    if (type == typeof(TeamMemberResolver))
        return new TeamMemberResolver(authorizationService, userClaimsService);
    if (type == typeof(PrimaryTeamResolver))
        return new PrimaryTeamResolver(authorizationService, userClaimsService);
    return Activator.CreateInstance(type)!;
});
```

Tests:
- `AutoMapper_Configuration_IsValid` - Creates mapper with resolver dependencies and verifies instantiation
- `AutoMapper_AllProfiles_AreRegistered` - Ensures all Profile classes are discovered

### Services/FileServiceTests.cs

Unit tests for `FileService` CRUD operations and authorization checks:

- `UploadAsync_NullTeamIds_ThrowsForbiddenException` - Validates team ID requirement
- `GetAsync_UnauthorizedUser_ThrowsForbiddenException` - Tests permission enforcement
- `GetAsync_AuthorizedUser_ReturnsFiles` - Happy path file retrieval
- `GetByIdAsync_FileNotFound_ThrowsEntityNotFoundException` - Not found handling
- `DeleteAsync_FileNotFound_ThrowsEntityNotFoundException` - Delete validation
- `DeleteAsync_UnauthorizedUser_ThrowsForbiddenException` - Delete authorization

Uses:
- `TestDbContextFactory.Create<PlayerContext>()` for InMemory EF Core database
- `FakeItEasy` for mocking `IPlayerAuthorizationService`, `IMapper`, `ITeamService`
- `PlayerCustomization` from Shared project for entity generation

### Services/TeamServiceTests.cs

Unit tests for `TeamService` team management operations:

- `GetByViewIdForCurrentUserAsync_ViewDoesNotExist_ThrowsEntityNotFoundException`
- `GetByViewIdForCurrentUserAsync_UserDoesNotExist_ThrowsEntityNotFoundException`
- `GetByViewIdForCurrentUserAsync_AuthorizedUser_ReturnsAllTeamsInView`

This test class creates a **real AutoMapper instance** (not a fake) with actual mapping profiles to test the full mapping pipeline including custom resolvers:

```csharp
var config = new MapperConfiguration(cfg =>
{
    cfg.ConstructServicesUsing(type =>
    {
        if (type == typeof(TeamMemberResolver))
            return new TeamMemberResolver(authService, userClaimsService);
        if (type == typeof(PrimaryTeamResolver))
            return new PrimaryTeamResolver(authService, userClaimsService);
        return Activator.CreateInstance(type)!;
    });

    cfg.AddProfile<Player.Api.Features.Teams.MappingProfile>();
    cfg.AddProfile<Player.Api.Features.TeamRoles.MappingProfile>();
    cfg.AddProfile<Player.Api.Features.TeamPermissions.MappingProfile>();
});

_mapper = config.CreateMapper();
```

## Running Tests

From the `player.api` directory:

```bash
# Run all unit tests
dotnet test Player.Api.Tests.Unit/Player.Api.Tests.Unit.csproj

# Run specific test class
dotnet test Player.Api.Tests.Unit/Player.Api.Tests.Unit.csproj --filter "FullyQualifiedName~FileServiceTests"

# Run specific test method
dotnet test Player.Api.Tests.Unit/Player.Api.Tests.Unit.csproj --filter "FullyQualifiedName~FileServiceTests.GetAsync_AuthorizedUser_ReturnsFiles"

# Run with coverage
dotnet test Player.Api.Tests.Unit/Player.Api.Tests.Unit.csproj --collect:"XPlat Code Coverage"
```

## Key Patterns

### Test Setup

All tests use constructor-based setup with:
- AutoFixture with `PlayerCustomization` for entity generation
- `AutoFakeItEasyCustomization` for automatic mock generation
- `TestDbContextFactory` for InMemory Entity Framework context
- FakeItEasy for explicit mocking

### Assertion Library

Tests use **Shouldly** for fluent assertions:

```csharp
result.ShouldNotBeNull();
result.Count().ShouldBe(2);
await Should.ThrowAsync<ForbiddenException>(() => _sut.MethodAsync());
```

### Authorization Testing

Player API uses `IPlayerAuthorizationService` for hierarchical permission checks (System, View, Team levels):

```csharp
A.CallTo(() => _authorizationService.Authorize<ViewEntity>(
    viewId,
    A<SystemPermission[]>._,
    A<ViewPermission[]>._,
    A<TeamPermission[]>._,
    A<CancellationToken>._))
    .Returns(false);
```

## Dependencies

- AutoFixture 4.18.1 + AutoFakeItEasy
- FakeItEasy 8.3.0
- Microsoft.EntityFrameworkCore.InMemory 10.0.1
- Shouldly 4.2.1
- xUnit 2.9.3
- MockQueryable.FakeItEasy 7.0.3
- Player.Api
- Player.Api.Data
- Player.Api.Tests.Shared
- Crucible.Common.Testing

## Coverage Target

Minimum 80% code coverage across all service layer methods.
