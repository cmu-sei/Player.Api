// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using AutoMapper.Internal;

namespace Player.Api.Tests.Support;

/// <summary>
/// The application's real AutoMapper configuration, built without starting the application.
/// </summary>
/// <remarks>
/// This mirrors <c>Startup.ConfigureServices</c>'s <c>AddAutoMapper</c> call: it scans
/// <c>Player.Api</c> for <see cref="Profile"/> classes and applies the same global
/// null-source-value convention. Using the real configuration rather than a substituted
/// <see cref="IMapper"/> means a handler test fails when a profile is wrong — which is the
/// mapping bug most likely to reach production, since nothing else exercises the profiles.
/// </remarks>
public static class TestMapper
{
    private static readonly Lazy<MapperConfiguration> LazyConfiguration = new(BuildConfiguration);
    private static readonly Lazy<IMapper> LazyMapper = new(() => LazyConfiguration.Value.CreateMapper());

    /// <summary>
    /// The shared configuration. Building it scans an assembly and compiles every map, so it is
    /// built once for the whole test run.
    /// </summary>
    public static MapperConfiguration Configuration => LazyConfiguration.Value;

    /// <summary>
    /// A mapper over <see cref="Configuration"/>. Mappers are thread-safe and stateless, so tests
    /// share one.
    /// </summary>
    public static IMapper Mapper => LazyMapper.Value;

    /// <summary>
    /// A mapper carrying the application's global conventions but only the maps
    /// <paramref name="configure"/> adds. Use this to assert on a convention in isolation, without
    /// depending on which production types happen to exercise it.
    /// </summary>
    public static IMapper CreateMapperWith(Action<IMapperConfigurationExpression> configure) =>
        new MapperConfiguration(cfg =>
        {
            configure(cfg);
            ApplyConventions(cfg);
        }).CreateMapper();

    private static MapperConfiguration BuildConfiguration() =>
        new(cfg =>
        {
            // Startup passes typeof(Startup) as the marker type, which scans that assembly for
            // Profile classes.
            cfg.AddMaps(typeof(Player.Api.Startup).Assembly);
            ApplyConventions(cfg);
        });

    /// <summary>
    /// The global conventions from <c>Startup.ConfigureServices</c>. Must stay in step with the
    /// <c>AddAutoMapper</c> call there.
    /// </summary>
    private static void ApplyConventions(IMapperConfigurationExpression cfg) =>
        cfg.Internal().ForAllPropertyMaps(
            pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
            (pm, c) => c.MapFrom<object, object, object, object>(
                new IgnoreNullSourceValues(), pm.SourceMember.Name));

    /// <summary>
    /// A local copy of <c>Player.Api.Infrastructure.Mappings.IgnoreNullSourceValues</c>, which is
    /// internal to <c>Player.Api</c>. Duplicated rather than made public — or reached via
    /// <c>InternalsVisibleTo</c> — so the production assembly's surface stays unchanged by the
    /// existence of tests. <see cref="MappingConfigurationTests"/> guards the duplication.
    /// </summary>
    private sealed class IgnoreNullSourceValues : IMemberValueResolver<object, object, object, object>
    {
        public object Resolve(
            object source,
            object destination,
            object sourceMember,
            object destinationMember,
            ResolutionContext context)
        {
            return sourceMember ?? destinationMember;
        }
    }
}
