# Dependency deferrals

`Directory.Build.props` leaves NuGet audit warnings visible without making them build errors. This file records the known exceptions so they have an explicit review trigger rather than living only in package comments.

Run the audit before changing this list:

```bash
dotnet list Player.Api.sln package --vulnerable --include-transitive
```

## AutoMapper 13

AutoMapper 13.0.1 has a high-severity advisory. The application is pinned because supported later releases require a commercial license.

Revisit when the project approves a licensed release or replaces AutoMapper. Any migration must cover the mapping configuration tests and the custom resolver registrations in the test host.

## MediatR 12

MediatR 12.4.1 is pinned because version 13 and later require a commercial license.

Revisit when the project approves a licensed release or replaces MediatR. The HTTP tests intentionally encode requests rather than MediatR calls, which limits the application-facing migration surface.

## Web code generation tooling

`Microsoft.VisualStudio.Web.CodeGeneration.Design` is unused runtime scaffolding but currently affects the resolved dependency graph. Version 10.0.2 resolves a patched `Microsoft.Build`, but it still carries `NuGet.Packaging` and `NuGet.Protocol` 6.12.1, which the security pins in `Player.Api.csproj` raise to 6.12.5. Earlier attempts to isolate it changed other transitive versions, so removal belongs in a dedicated dependency change with a full restore, build, test and audit comparison.

Revisit by removing the package rather than pinning around it. Confirm that generated-code workflows are not used and that the resulting graph does not introduce older transitive packages. Removing it also removes the compile-time Roslyn, MSBuild and NuGet assemblies it brings in, which the application leans on by accident:

- `ServiceCollectionExtensions` maps `Microsoft.CodeAnalysis.Optional<Guid?>` in Swagger, a Roslyn type that no model uses.
- `GetNotifications` and `PresenceService` import `Microsoft.Build.Framework`, and `ImportApplicationTemplates` imports `NuGet.Packaging`.
- `Player.Api.Tests` loses the Newtonsoft.Json 13.x the package supplies and falls back to the vulnerable 11.0.2 that TinCan and IdentityServer4.AccessTokenValidation ask for, so it needs a pin of its own.

## Reviewing warnings

This list is not a blanket acceptance of every NU1901–NU1904 warning. New advisories or newly affected packages require their own decision. Update this file when a deferral is added, resolved or materially changes.
