// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;

namespace Player.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// Covers <see cref="TeamPermissionsClaim"/>, the wire format for every team-scoped permission
/// decision: it is serialized into a claim value at sign-in and parsed back on every request.
/// </summary>
public class TeamPermissionsClaimTests
{
    [Fact]
    public void Round_trips_every_stored_field()
    {
        var claim = new TeamPermissionsClaim
        {
            ViewId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            IsPrimary = true,
            PermissionValues = [ViewPermission.ViewView.ToString(), TeamPermission.ManageTeam.ToString()],
            DirectPermissionValues = [TeamPermission.ViewTeam.ToString()],
            SourceTeamIds = [Guid.NewGuid(), Guid.NewGuid()]
        };

        var restored = TeamPermissionsClaim.FromString(claim.ToString());

        Assert.Equal(claim.ViewId, restored.ViewId);
        Assert.Equal(claim.TeamId, restored.TeamId);
        Assert.Equal(claim.IsPrimary, restored.IsPrimary);
        Assert.Equal(claim.PermissionValues, restored.PermissionValues);
        Assert.Equal(claim.DirectPermissionValues, restored.DirectPermissionValues);
        Assert.Equal(claim.SourceTeamIds, restored.SourceTeamIds);
    }

    [Fact]
    public void Splits_mixed_permission_values_by_enum()
    {
        // The two enums share one flat list on the wire, so each projection must pick out only its own
        // members — a ViewPermission must never be readable as a TeamPermission.
        var claim = new TeamPermissionsClaim
        {
            PermissionValues =
            [
                ViewPermission.ViewView.ToString(),
                ViewPermission.ManageView.ToString(),
                TeamPermission.EditTeam.ToString()
            ]
        };

        Assert.Equal([ViewPermission.ViewView, ViewPermission.ManageView], claim.ViewPermissions);
        Assert.Equal([TeamPermission.EditTeam], claim.TeamPermissions);
    }

    [Fact]
    public void Reads_direct_projections_from_the_direct_values_only()
    {
        // The distinction the scoped-team-permissions feature turns on: a permission can be effective
        // without being held directly, and GetPrimaryVisibilityContext branches on exactly that.
        var claim = new TeamPermissionsClaim
        {
            PermissionValues = [ViewPermission.ManageView.ToString(), TeamPermission.ManageTeam.ToString()],
            DirectPermissionValues = [ViewPermission.ViewView.ToString(), TeamPermission.ViewTeam.ToString()]
        };

        Assert.Equal([ViewPermission.ManageView], claim.ViewPermissions);
        Assert.Equal([TeamPermission.ManageTeam], claim.TeamPermissions);
        Assert.Equal([ViewPermission.ViewView], claim.DirectViewPermissions);
        Assert.Equal([TeamPermission.ViewTeam], claim.DirectTeamPermissions);
    }

    [Fact]
    public void Drops_permission_values_that_are_not_known_enum_members()
    {
        // A permission removed from the enum but still present in a role in the database must not take
        // the request down.
        var claim = new TeamPermissionsClaim
        {
            PermissionValues = ["RetiredPermission", TeamPermission.ViewTeam.ToString()],
            DirectPermissionValues = ["RetiredPermission"]
        };

        Assert.Equal([TeamPermission.ViewTeam], claim.TeamPermissions);
        Assert.Empty(claim.ViewPermissions);
        Assert.Empty(claim.DirectTeamPermissions);
        Assert.Empty(claim.DirectViewPermissions);
    }

    [Fact]
    public void Does_not_serialize_the_computed_projections()
    {
        // Claim values ride in a token on every request; serializing the four derived arrays as well
        // would duplicate the permission lists three times over.
        var claim = new TeamPermissionsClaim
        {
            PermissionValues = [TeamPermission.ViewTeam.ToString()]
        };

        using var document = JsonDocument.Parse(claim.ToString());
        var properties = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();

        Assert.DoesNotContain(nameof(TeamPermissionsClaim.TeamPermissions), properties);
        Assert.DoesNotContain(nameof(TeamPermissionsClaim.ViewPermissions), properties);
        Assert.DoesNotContain(nameof(TeamPermissionsClaim.DirectTeamPermissions), properties);
        Assert.DoesNotContain(nameof(TeamPermissionsClaim.DirectViewPermissions), properties);
        Assert.Contains(nameof(TeamPermissionsClaim.PermissionValues), properties);
    }

    [Fact]
    public void Defaults_every_array_to_empty_rather_than_null()
    {
        var claim = new TeamPermissionsClaim();

        Assert.Empty(claim.PermissionValues);
        Assert.Empty(claim.DirectPermissionValues);
        Assert.Empty(claim.SourceTeamIds);
        Assert.Empty(claim.TeamPermissions);
        Assert.Empty(claim.ViewPermissions);
    }

    /// <summary>
    /// A payload written before <c>SourceTeamIds</c> existed omits the property, and
    /// <c>JsonSerializer</c> leaves an absent property untouched rather than nulling it — so the
    /// property initializer holds and old claims stay readable.
    /// </summary>
    [Fact]
    public void Tolerates_a_payload_that_omits_source_team_ids()
    {
        var json = $$"""{"ViewId":"{{Guid.Empty}}","TeamId":"{{Guid.Empty}}","PermissionValues":["ViewTeam"]}""";

        var claim = TeamPermissionsClaim.FromString(json);

        Assert.Empty(claim.SourceTeamIds);
        Assert.Equal([TeamPermission.ViewTeam], claim.TeamPermissions);
    }
}
