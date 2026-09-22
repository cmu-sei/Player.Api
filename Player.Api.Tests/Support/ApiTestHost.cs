// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using System.Security.Principal;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Data.Data;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Options;
using Player.Api.Services;
using AuthorizationOptions = Player.Api.Options.AuthorizationOptions;
using XApiOptions = Player.Api.Infrastructure.Options.XApiOptions;

namespace Player.Api.Tests.Support;

/// <summary>
/// The application's services without the web host: real services, real AutoMapper profiles and the
/// real authorization stack, over one test's database and acting as one test's user. Resolve something
/// out of it and call the service directly.
/// </summary>
/// <remarks>
/// <para>
/// This is not how a request is tested — <see cref="ApiTestBase"/> is, over HTTP. What is left here is
/// the case that has no request to send: constructing a service with options a test chose, which
/// configuration supplies for the whole run in the hosted application.
/// </para>
/// <para>
/// The registrations mirror <c>Startup.ConfigureServices</c>, MediatR included, which is what lets
/// <see cref="ApiTestHostTests"/> construct every handler in the assembly. A production dependency the
/// container does not register then fails one test by name — including for the handlers no test sends a
/// request to yet.
/// </para>
/// <para>
/// Everything shares one <see cref="PlayerContext"/>, as a request does. Its <c>ServiceProvider</c>
/// still points at the session's provider, so entity events reach
/// <see cref="DatabaseTestBase.Mediator"/> rather than the real handlers — those are covered directly,
/// and firing them here would rebuild an auth cache on every save.
/// </para>
/// </remarks>
public sealed class ApiTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    private ApiTestHost(ServiceProvider services, ServiceDescriptor[] registrations)
    {
        _services = services;
        Registrations = registrations;
    }

    /// <summary>
    /// What this host registered, for the tests that compare it with the hosted application's own
    /// composition.
    /// </summary>
    public IReadOnlyList<ServiceDescriptor> Registrations { get; }

    /// <summary>The substituted view hub, for asserting on broadcast notifications.</summary>
    public IHubContext<ViewHub> ViewHub => Resolve<IHubContext<ViewHub>>();

    public T Resolve<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>
    /// The container itself, for the few pieces of production code that take a provider rather than its
    /// contents — <c>DatabaseExtensions.InitializeDatabase</c> resolves out of one.
    /// </summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// Builds a host over <paramref name="db"/> acting as <paramref name="user"/>.
    /// <paramref name="configure"/> adjusts the options the application reads from configuration.
    /// </summary>
    public static ApiTestHost Create(
        PlayerContext db,
        ClaimsPrincipal user,
        Action<ApiTestHostOptions> configure = null)
    {
        var options = new ApiTestHostOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddMemoryCache();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Startup>());

        // Registered as an instance rather than a factory: the test owns this context's lifetime,
        // and a factory registration would have the container dispose it out from under the test.
        services.AddSingleton(db);

        AddMapper(services);
        AddCurrentUser(services, user);
        AddOptions(services, options);
        AddApplicationServices(services);
        AddSubstitutedCollaborators(services);
        AddAuthorization(services);

        return new ApiTestHost(services.BuildServiceProvider(), [.. services]);
    }

    public void Dispose() => _services.Dispose();

    /// <summary>
    /// The real profiles, so a handler test fails on a broken map.
    /// </summary>
    /// <remarks>
    /// Built over the container rather than reusing the shared <see cref="TestMapper.Mapper"/> because
    /// some maps delegate a member to a value resolver with constructor dependencies —
    /// <c>Teams.MappingProfile</c> resolves <c>IsMember</c> and <c>IsPrimary</c> through the
    /// authorization stack. Registering the resolvers is what <c>AddAutoMapper</c> does in production;
    /// without it AutoMapper falls back to <see cref="Activator"/> and the map fails on the missing
    /// parameterless constructor.
    /// </remarks>
    private static void AddMapper(IServiceCollection services)
    {
        foreach (var type in typeof(Startup).Assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface))
        {
            foreach (var contract in type.GetInterfaces().Where(IsMappingContract))
            {
                services.AddTransient(contract, type);
                services.AddTransient(type);
            }
        }

        services.AddScoped<IMapper>(p => new Mapper(TestMapper.Configuration, p.GetService));
    }

    private static readonly Type[] MappingContracts =
    [
        typeof(IValueResolver<,,>),
        typeof(IMemberValueResolver<,,,>),
        typeof(ITypeConverter<,>),
        typeof(IMappingAction<,>)
    ];

    private static bool IsMappingContract(Type contract) =>
        contract.IsGenericType && MappingContracts.Contains(contract.GetGenericTypeDefinition());

    /// <summary>
    /// Production reads the current user off the <see cref="HttpContext"/>, and so do
    /// <c>IdentityResolver</c> and everything taking a bare <see cref="IPrincipal"/>. A real accessor
    /// over a <see cref="DefaultHttpContext"/> keeps <c>IdentityResolver</c> under test rather than
    /// substituted away.
    /// </summary>
    private static void AddCurrentUser(IServiceCollection services, ClaimsPrincipal user)
    {
        services.AddSingleton<IHttpContextAccessor>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } });
        services.AddScoped<IPrincipal>(p => p.GetRequiredService<IHttpContextAccessor>().HttpContext.User);
        services.AddScoped<IIdentityResolver, IdentityResolver>();
    }

    private static void AddOptions(IServiceCollection services, ApiTestHostOptions options)
    {
        services.AddSingleton(options.Roles);
        services.AddSingleton(options.FileUpload);
        services.AddSingleton(options.ClaimsTransformation);
        services.AddSingleton(options.XApi);
        services.AddSingleton(options.Authorization);
        services.AddSingleton(options.Database);
        services.AddSingleton(options.SeedData);
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(options.Configuration).Build());
    }

    private static void AddApplicationServices(IServiceCollection services)
    {
        services.AddScoped<IPlayerAuthorizationService, AuthorizationService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<IArchiveService, ArchiveService>();
        // Seeded as AuthorizationClaimsTransformer seeds it at request time. No transformer runs here,
        // so readers — the Teams profile's value resolvers — would otherwise get null.
        services.AddScoped<IUserClaimsService>(p =>
        {
            var claims = ActivatorUtilities.CreateInstance<UserClaimsService>(p);
            claims.SetCurrentClaimsPrincipal(p.GetRequiredService<IHttpContextAccessor>().HttpContext.User);
            return claims;
        });
        services.AddScoped<IXApiQueueService, XApiQueueService>();
        services.AddScoped<IXApiService, XApiService>();
        services.AddScoped<Player.Api.Features.Views.ViewImporter>();
        services.AddSingleton<ConnectionCacheService>();
        services.AddSingleton<TelemetryService>();
    }

    /// <summary>
    /// The collaborators that leave the process. Each is a substitute a test can assert against.
    /// </summary>
    private static void AddSubstitutedCollaborators(IServiceCollection services)
    {
        services.AddSingleton(Substitute.For<IHubContext<ViewHub>>());
        services.AddSingleton(Substitute.For<IHubContext<TeamHub>>());
        services.AddSingleton(Substitute.For<IHubContext<UserHub>>());
        services.AddSingleton(Substitute.For<IBackgroundWebhookService>());
        services.AddSingleton(Substitute.For<IHttpClientFactory>());
    }

    /// <summary>
    /// <c>Startup.ApplyPolicies</c>'s handler registrations. The named policies are unnecessary —
    /// nothing routes through an endpoint filter, and <c>AuthorizationService</c> evaluates
    /// requirements directly.
    /// </summary>
    private static void AddAuthorization(IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, SystemPermissionsHandler>();
        services.AddSingleton<IAuthorizationHandler, TeamPermissionsHandler>();
        services.AddSingleton<IAuthorizationHandler, ViewMemberHandler>();
        services.AddSingleton<IAuthorizationHandler, TeamMemberHandler>();
        services.AddSingleton<IAuthorizationHandler, PrimaryTeamHandler>();
    }
}

/// <summary>
/// The configuration-backed options an <see cref="ApiTestHost"/> exposes, defaulted to values that
/// let every handler run. Tests override only what they are asserting on.
/// </summary>
public sealed class ApiTestHostOptions
{
    /// <summary>
    /// Names seeded roles. <c>DefaultTeamRole</c> has to resolve to a seeded row, or a service that
    /// falls back to it fails.
    /// </summary>
    public RoleOptions Roles { get; } = new()
    {
        DefaultTeamRole = "View Member",
        DefaultViewCreatorRole = "View Admin"
    };

    public FileUploadOptions FileUpload { get; } = new()
    {
        basePath = Path.Combine(Path.GetTempPath(), $"player-tests-{Guid.NewGuid():N}"),
        maxSize = 1024 * 1024,
        allowedExtensions = [".txt", ".png", ".pdf"]
    };

    public ClaimsTransformationOptions ClaimsTransformation { get; } = new()
    {
        EnableCaching = false,
        CacheExpirationSeconds = 60,
        UseRolesFromIdP = false,
        RolesClaimPath = "realm_access.roles"
    };

    /// <summary>
    /// Left unconfigured: an empty <c>Username</c> is how <c>XApiService</c> knows xAPI is off.
    /// </summary>
    public XApiOptions XApi { get; } = new();

    public AuthorizationOptions Authorization { get; } = new();

    /// <summary>
    /// Left at its defaults, which is what a deployed instance uses: no recreate, no seeding.
    /// <c>DatabaseExtensions</c> tests set them.
    /// </summary>
    public DatabaseOptions Database { get; } = new();

    public SeedDataOptions SeedData { get; } = new();

    public Dictionary<string, string> Configuration { get; } = new()
    {
        ["Notifications:SystemIconUrl"] = "https://example.test/system.png",
        ["Notifications:UserIconUrl"] = "https://example.test/user.png"
    };
}
