// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Options;
using Player.Api.Services;
using XApiOptions = Player.Api.Infrastructure.Options.XApiOptions;

namespace Player.Api.Tests.Support;

/// <summary>
/// Compares what <see cref="ApiTestHost"/> registers with what the hosted application registers, so that
/// a service test cannot quietly be run against a container the application does not have.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ApiTestHost"/> is written out by hand, so a service added to <c>Startup</c> is not added
/// here and a lifetime changed there is not changed here. Nothing fails when the two drift: the service
/// tests keep passing against the composition they already had, and what breaks is a request or a
/// deployment — the place the service test was supposed to speak for. These tests are that drift, in one
/// place, naming the service.
/// </para>
/// <para>
/// What is compared is registrations: whether a service type is there, what lifetime it has, and which
/// implementation answers for it. What each service then does is every other test in the suite.
/// </para>
/// <para>
/// Only the application's own registrations, on both sides. The frameworks' are noise — the two
/// containers are built by different means, one over a web host and one over a bare collection, and were
/// never going to agree on them.
/// </para>
/// <para>
/// Both sides are read with <c>open-api-only</c> set (see <see cref="PlayerAppFactory"/>), which gates
/// off the two <c>IHostedService</c> registrations, so nothing here says anything about those.
/// <c>BackgroundWebhookServiceTests</c> and <c>XApiBackgroundServiceTests</c> drive them directly.
/// </para>
/// </remarks>
public class ApiTestHostCompositionTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ServiceTestBase(fixture)
{
    private static readonly Assembly Application = typeof(Startup).Assembly;

    /// <summary>
    /// What the application registers and this host deliberately does not, each with the reason. The
    /// table is the point of these tests: it makes every omission something somebody decided, and
    /// <see cref="Nothing_is_excused_that_the_comparison_would_have_passed"/> keeps it from going stale.
    /// </summary>
    private static readonly (Func<Type, bool> Matches, string Reason)[] NotMirrored =
    [
        (x => typeof(IEndpoint).IsAssignableFrom(x),
            "An endpoint class per route. Reaching one takes the routing table and the middleware chain, " +
            "which is what ApiTestBase hosts."),

        (x => x == typeof(IClaimsTransformation),
            "AuthorizationClaimsTransformer runs during authentication, and there is no request here. " +
            "This host seeds IUserClaimsService with the principal instead, which is the state the " +
            "transformer would have left; the transformer itself is covered over HTTP."),

        (x => x == typeof(BackgroundWebhookService),
            "A hosted singleton, and its tests construct it directly with the logger and the HTTP client " +
            "they assert against. Registering it would only offer a second, differently wired instance."),

        (x => x == typeof(AppOptions),
            "Read by ApplicationInstanceFilter alone, an IEndpointFilter the endpoint pipeline " +
            "constructs. Its tests construct the filter with the options they are asserting on.")
    ];

    /// <summary>
    /// The options a test chooses before its host is built. The application binds each from configuration
    /// per scope; here each is one instance for the host's lifetime, which is what makes
    /// <c>HostFor(user, o =&gt; …)</c> mean anything.
    /// </summary>
    private static readonly Type[] PerTestOptions =
    [
        typeof(RoleOptions),
        typeof(FileUploadOptions),
        typeof(ClaimsTransformationOptions),
        typeof(XApiOptions),
        typeof(DatabaseOptions),
        typeof(SeedDataOptions)
    ];

    /// <summary>
    /// A service the application registers and this host does not is the drift that costs the most: the
    /// service test that would have resolved it never runs, so the gap shows up as a failure somewhere
    /// else or as no failure at all.
    /// </summary>
    [Fact]
    public void Every_service_the_application_registers_is_registered_here_too()
    {
        var application = ByServiceType(Production);
        var here = ByServiceType(TestHost).Keys.ToHashSet();

        // A snapshot that captured nothing would make every test in this class pass.
        Assert.NotEmpty(application);

        var missing = application.Keys
            .Where(x => !here.Contains(x))
            .Where(x => !NotMirrored.Any(y => y.Matches(x)))
            .Select(Describe)
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} service(s) the application registers are missing from ApiTestHost. Register " +
            $"them, or add them to NotMirrored with the reason:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// An excuse for something the application no longer registers, or for something this host has since
    /// gained, is an excuse that hides the next real gap.
    /// </summary>
    [Fact]
    public void Nothing_is_excused_that_the_comparison_would_have_passed()
    {
        var application = ByServiceType(Production).Keys.ToList();
        var here = ByServiceType(TestHost).Keys.ToList();

        Assert.All(NotMirrored, x =>
        {
            Assert.True(
                application.Any(x.Matches),
                $"The application registers nothing this entry matches: {x.Reason}");

            Assert.False(here.Any(x.Matches), $"This is registered here after all: {x.Reason}");
        });
    }

    /// <summary>
    /// A lifetime is a behaviour. A service the application scopes to a request and this host holds as a
    /// singleton keeps state across a test's calls that production would have thrown away — and the
    /// reverse loses state the production service is entitled to keep.
    /// </summary>
    [Fact]
    public void A_mirrored_service_keeps_the_lifetime_the_application_gives_it()
    {
        var here = ByServiceType(TestHost);

        var differs = ByServiceType(Production)
            .Where(x => here.ContainsKey(x.Key))
            .Where(x => !PerTestOptions.Contains(x.Key))
            .Where(x => !Lifetimes(x.Value).SetEquals(Lifetimes(here[x.Key])))
            .Select(x => $"{Describe(x.Key)}: {Join(Lifetimes(here[x.Key]))} here, " +
                $"{Join(Lifetimes(x.Value))} in the application")
            .Order()
            .ToList();

        Assert.True(
            differs.Count == 0,
            $"{differs.Count} service(s) are registered here with a lifetime the application does not " +
            $"give them:{Environment.NewLine}{string.Join(Environment.NewLine, differs)}");
    }

    /// <summary>
    /// The one lifetime difference there is, asserted from both sides rather than merely excused above:
    /// the options classes.
    /// </summary>
    /// <remarks>
    /// Stated this way, the exemption cannot quietly grow into cover for a service that drifted. If the
    /// application ever binds one of these per request from something a request carries, this fails and
    /// the per-host instance stops being a fair stand-in.
    /// </remarks>
    [Fact]
    public void The_options_a_test_chooses_are_the_only_lifetime_that_differs()
    {
        var application = ByServiceType(Production);
        var here = ByServiceType(TestHost);

        Assert.All(PerTestOptions, type =>
        {
            Assert.True(
                application.ContainsKey(type) && here.ContainsKey(type),
                $"{Describe(type)} is no longer registered on both sides.");

            Assert.Equal(ServiceLifetime.Scoped, Assert.Single(application[type]).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, Assert.Single(here[type]).Lifetime);
        });
    }

    /// <summary>
    /// A service type registered here against something other than the application's own implementation
    /// is the drift a lifetime check cannot see: the test resolves what it asked for, and it is not the
    /// class production runs.
    /// </summary>
    /// <remarks>
    /// Resolved rather than read off the descriptor, because a registration this host makes as a factory
    /// — <c>IUserClaimsService</c>, which is seeded with the principal — names no implementation type to
    /// compare. What comes out of the container is the answer either way. Only the service types the
    /// application registers exactly once are asked; the collections are covered by
    /// <see cref="Every_authorization_handler_the_application_applies_is_applied_here"/> and by
    /// <see cref="ApiTestHostTests.Every_handler_the_application_registers_can_be_constructed"/>.
    /// </remarks>
    [Fact]
    public void A_mirrored_service_resolves_to_the_implementation_the_application_names()
    {
        var here = ByServiceType(TestHost);

        var comparable = ByServiceType(Production)
            .Where(x => here.ContainsKey(x.Key))
            .Where(x => x.Value.Count == 1 && x.Value[0].ImplementationType != null)
            .ToList();

        // The filter is narrow enough to empty itself if descriptor shapes change, which would leave
        // nothing asserted.
        Assert.NotEmpty(comparable);

        var wrong = comparable
            .Select(x => new
            {
                Service = x.Key,
                Expected = x.Value[0].ImplementationType,
                Actual = RootHost.Services.GetService(x.Key)?.GetType()
            })
            .Where(x => x.Actual != x.Expected)
            .Select(x => $"{Describe(x.Service)}: {x.Actual?.Name ?? "nothing"} rather than {x.Expected.Name}")
            .Order()
            .ToList();

        Assert.True(
            wrong.Count == 0,
            $"{wrong.Count} service(s) resolve here to something the application does not register:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>
    /// The handlers are a collection, so a missing one is not a resolution failure. The requirement it
    /// answers is simply never satisfied, and an authorization test then asserts a denial that production
    /// allows.
    /// </summary>
    [Fact]
    public void Every_authorization_handler_the_application_applies_is_applied_here()
    {
        Assert.Equal(Handlers(Production), Handlers(TestHost));
    }

    /// <summary>
    /// The reverse direction, which matters less but not none: something registered only here is a
    /// dependency a handler can take without production being able to supply it.
    /// </summary>
    /// <remarks>
    /// AutoMapper's resolver contracts are the whole exemption. <c>ApiTestHost.AddMapper</c> registers the
    /// application's value resolvers under those as well as under their own types; the application does
    /// not have to, because its mapper asks for the concrete type. See that method's remarks.
    /// </remarks>
    [Fact]
    public void Nothing_beyond_the_mapping_contracts_is_registered_here_alone()
    {
        var application = ByServiceType(Production).Keys.ToHashSet();
        var here = ByServiceType(TestHost).Keys.ToList();

        Assert.NotEmpty(here);

        Assert.All(
            here.Where(x => !application.Contains(x)),
            x => Assert.True(
                IsMappingContract(x),
                $"{Describe(x)} is registered here and nowhere in the application."));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// What <c>Startup.ConfigureServices</c> left behind, before the factory's own replacements.
    /// </summary>
    private IReadOnlyList<ServiceDescriptor> Production => Own(factory.ProductionRegistrations);

    /// <summary>What <see cref="ApiTestHost"/> registered.</summary>
    private IReadOnlyList<ServiceDescriptor> TestHost => Own(RootHost.Registrations);

    /// <summary>
    /// The application's own registrations: those it declares, plus those it contributes an
    /// implementation to under a framework contract — <c>IAuthorizationHandler</c>, the MediatR handlers.
    /// </summary>
    private static List<ServiceDescriptor> Own(IEnumerable<ServiceDescriptor> registrations) =>
        [.. registrations.Where(x =>
            x.ServiceType.Assembly == Application || x.ImplementationType?.Assembly == Application)];

    private static Dictionary<Type, List<ServiceDescriptor>> ByServiceType(
        IEnumerable<ServiceDescriptor> registrations) =>
        registrations.GroupBy(x => x.ServiceType).ToDictionary(x => x.Key, x => x.ToList());

    private static List<string> Handlers(IEnumerable<ServiceDescriptor> registrations) =>
        [.. registrations
            .Where(x => x.ServiceType == typeof(IAuthorizationHandler))
            .Select(x => x.ImplementationType.Name)
            .Order()];

    private static HashSet<ServiceLifetime> Lifetimes(IEnumerable<ServiceDescriptor> registrations) =>
        [.. registrations.Select(x => x.Lifetime)];

    private static string Join(IEnumerable<ServiceLifetime> lifetimes) =>
        string.Join('/', lifetimes.Order());

    private static readonly Type[] MappingContracts =
    [
        typeof(IValueResolver<,,>),
        typeof(IMemberValueResolver<,,,>),
        typeof(ITypeConverter<,>),
        typeof(IMappingAction<,>)
    ];

    private static bool IsMappingContract(Type type) =>
        type.IsGenericType && MappingContracts.Contains(type.GetGenericTypeDefinition());

    /// <summary>A service type as a failure message should name it.</summary>
    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GenericTypeArguments.Select(x => x.Name))}>"
            : type.Name;
}
