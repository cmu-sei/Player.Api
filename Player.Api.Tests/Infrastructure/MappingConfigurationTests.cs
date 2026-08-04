// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using AutoMapper.Internal;
using Player.Api.Data.Data.Models;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure;

/// <summary>
/// Guards the AutoMapper configuration, which is otherwise validated only at runtime, on the first
/// map of each type pair — so a broken profile surfaces as a 500 on whichever endpoint happens to use
/// it rather than at build time.
/// </summary>
public class MappingConfigurationTests
{
    /// <summary>
    /// Carries the production conventions but only this class's own types, so the convention tests do
    /// not depend on which production entity happens to have a nullable property today.
    /// </summary>
    private static readonly IMapper ConventionMapper =
        TestMapper.CreateMapperWith(cfg => cfg.CreateMap<Source, Destination>());

    /// <summary>
    /// Compiles every map. This is the whole-configuration check that <em>is</em> satisfiable today: it
    /// catches duplicate <c>CreateMap</c> calls for one type pair, member configurations naming a
    /// member that no longer exists, and expressions AutoMapper cannot build a plan for. It does not
    /// check that every destination member is mapped — see
    /// <see cref="Every_outbound_map_populates_every_destination_member"/> for that.
    /// </summary>
    [Fact]
    public void Every_map_compiles()
    {
        TestMapper.Configuration.CompileMappings();
    }

    /// <summary>
    /// Full member validation, restricted to the outbound direction — entity to view model — where an
    /// unmapped destination member means an API response silently carries a default value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inbound maps (request or command onto an entity) are excluded, because AutoMapper's default
    /// validation asks the wrong question of them: they leave <c>Id</c>, EF navigation properties,
    /// server-assigned foreign keys and fields like <c>DateCreated</c> alone entirely on purpose. The
    /// question worth asking of that direction is the mirror image, and
    /// <see cref="Every_source_validated_map_consumes_every_source_member"/> asks it.
    /// </para>
    /// <para>
    /// The member list is matched positively — <c>== MemberList.Destination</c>, not
    /// <c>!= MemberList.Source</c>. They are not the same filter, because <c>MemberList.None</c> is a
    /// third case that <see cref="AssertAllValid"/> cannot check at all, and admitting one would put a
    /// map in this test's set that it silently does not validate. See
    /// <see cref="No_map_opts_out_of_member_validation_unexpectedly"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_outbound_map_populates_every_destination_member()
    {
        var configuration = TestMapper.Configuration.Internal();
        var outbound = configuration.GetAllTypeMaps()
            .Where(x => !typeof(IEntity).IsAssignableFrom(x.DestinationType))
            .Where(x => x.ConfiguredMemberList == MemberList.Destination)
            .ToList();

        // A filter that matched nothing would make this test pass without asserting anything.
        Assert.NotEmpty(outbound);

        AssertAllValid(configuration, outbound);
    }

    /// <summary>
    /// Pins the maps that neither of the two validation tests can check, so the blind spot stays the
    /// size it is instead of growing unnoticed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AssertConfigurationIsValid</c> is a no-op for a <c>MemberList.None</c> map — it reports
    /// nothing at all, even for a destination member that is plainly unmapped. So a map configured that
    /// way is exempt from member validation however the other tests filter, and only
    /// <see cref="Every_map_compiles"/> still covers it.
    /// </para>
    /// <para>
    /// Nothing in this application asks for <c>MemberList.None</c>; every entry below is a
    /// <c>ReverseMap()</c> product, which is how AutoMapper configures the reversed direction. That is
    /// worth knowing before converting the remaining inbound maps to <c>MemberList.Source</c>: three of
    /// these are inbound, and they cannot opt in without unwinding the <c>ReverseMap()</c> that created
    /// them into two explicit <c>CreateMap</c> calls.
    /// </para>
    /// <para>
    /// Adding a <c>ReverseMap()</c> will fail this test. That is the intent — the new pair needs a
    /// deliberate decision about which direction is validated, not a silent exemption.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_map_opts_out_of_member_validation_unexpectedly()
    {
        var unvalidatable = TestMapper.Configuration.Internal().GetAllTypeMaps()
            .Where(x => x.ConfiguredMemberList == MemberList.None)
            .Select(x => $"{x.SourceType.Name} -> {x.DestinationType.Name}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "ApplicationInstanceEntity -> ApplicationInstanceExport",
                "ApplicationTemplate -> ApplicationTemplateEntity",
                "FileModel -> FileEntity",
                "Notification -> NotificationEntity",
            ],
            unvalidatable);
    }

    /// <summary>
    /// The inbound counterpart: for every map that declared <c>MemberList.Source</c>, assert that each
    /// source member reaches the entity. A command property that stopped being mapped — renamed, or
    /// dropped from the profile — is otherwise discarded in silence, with the request appearing to
    /// succeed and the value never persisted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set is discovered from <see cref="TypeMap.ConfiguredMemberList"/> rather than listed here,
    /// so the profiles decide what is covered and this test cannot fall out of step with them. Adding
    /// <c>MemberList.Source</c> to a profile enrols that map with no test change; a member that is
    /// legitimately not persisted is declared on the map itself, as
    /// <c>ForSourceMember(x =&gt; x.Member, o =&gt; o.DoNotValidate())</c>.
    /// </para>
    /// <para>
    /// No profile declares <c>MemberList.Source</c> today, so the test skips itself rather than passing
    /// vacuously — an empty set would make it green while asserting nothing. It needs no edit to become
    /// live: the first <c>CreateMap&lt;T, TEntity&gt;(MemberList.Source)</c> anywhere in the application
    /// enrols that map and every one added after it.
    /// </para>
    /// <para>
    /// Until then the inbound direction is compiled by <see cref="Every_map_compiles"/> but not
    /// member-validated, and neither is any map outside
    /// <see cref="Every_outbound_map_populates_every_destination_member"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_source_validated_map_consumes_every_source_member()
    {
        var configuration = TestMapper.Configuration.Internal();
        var sourceValidated = configuration.GetAllTypeMaps()
            .Where(x => x.ConfiguredMemberList == MemberList.Source)
            .ToList();

        Assert.SkipWhen(
            sourceValidated.Count == 0,
            "No map declares MemberList.Source yet, so there is nothing to validate. This test starts " +
                "running on its own as soon as one does.");

        AssertAllValid(configuration, sourceValidated);
    }

    /// <summary>
    /// Validates the maps one at a time and collects the failures, so a run reports every broken map
    /// instead of only the first. Each map is checked against whichever member list it was configured
    /// with, which is what lets the two directions share this helper.
    /// </summary>
    private static void AssertAllValid(IGlobalConfiguration configuration, IEnumerable<TypeMap> typeMaps)
    {
        var failures = new List<string>();

        foreach (var typeMap in typeMaps)
        {
            try
            {
                configuration.AssertConfigurationIsValid(typeMap);
            }
            catch (AutoMapperConfigurationException ex)
            {
                failures.Add(ex.Message);
            }
        }

        // Not Assert.Empty: it renders the collection, and AutoMapper's messages are long enough that
        // every one of them gets ellipsised away — leaving a failure that says only "not empty". The
        // messages name the offending members, which is the entire value of this test failing.
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} map(s) failed validation:{Environment.NewLine}{Environment.NewLine}" +
                string.Join($"{Environment.NewLine}{Environment.NewLine}", failures));
    }

    /// <summary>
    /// Pins the global convention <c>Startup</c> installs: when a source property is a nullable
    /// version of its destination property and the source value is null, the destination keeps the
    /// value it already had rather than being overwritten with the type default. Every PATCH-style
    /// update request in the application depends on this.
    /// </summary>
    [Fact]
    public void A_null_nullable_source_value_leaves_the_destination_unchanged()
    {
        var destination = new Destination { Count = 42, Enabled = true };

        ConventionMapper.Map(new Source { Count = null, Enabled = null }, destination);

        Assert.Equal(42, destination.Count);
        Assert.True(destination.Enabled);
    }

    [Fact]
    public void A_present_nullable_source_value_overwrites_the_destination()
    {
        var destination = new Destination { Count = 42, Enabled = true };

        ConventionMapper.Map(new Source { Count = 7, Enabled = false }, destination);

        Assert.Equal(7, destination.Count);
        Assert.False(destination.Enabled);
    }

    private class Source
    {
        public int? Count { get; set; }
        public bool? Enabled { get; set; }
    }

    private class Destination
    {
        public int Count { get; set; }
        public bool Enabled { get; set; }
    }
}
