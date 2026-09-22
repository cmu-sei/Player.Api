// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Views;

/// <summary>
/// Covers the mapping half of view export and import: the two directions are separate profiles that
/// nothing forces to agree, so a member the export writes but the import cannot read is invisible
/// until an operator uploads a file.
/// </summary>
/// <remarks>
/// No database, and therefore an approximation in one place worth naming: <c>Export</c> reaches
/// <c>ViewExportDTO</c> with <c>ProjectTo</c>, which compiles the map into SQL, whereas this test uses
/// <c>Map</c>. The two engines can disagree — an expression LINQ cannot translate, or an
/// <c>IValueResolver</c>, works under one and not the other. They agree on the members this test cares
/// about, but a failure here does not rule out a <c>ProjectTo</c>-only problem, and covering that needs
/// a database. The second hop (<c>ViewExportDTO</c> to <c>ViewExport</c>) and the import are plain
/// <c>Map</c> calls in production too, so those are exact. The JSON hop <c>Import</c> performs is also
/// not covered.
/// </remarks>
public class ViewExportRoundTripTests
{
    /// <summary>
    /// Exports a view whose teams carry direct permission assignments and maps it back, pinning what the
    /// return trip does.
    /// </summary>
    /// <remarks>
    /// <c>TeamEntity.Permissions</c> holds <c>TeamPermissionAssignmentEntity</c> rows, and the outbound
    /// map flattens them to the <c>TeamPermissionModel</c> definitions behind them — so the two
    /// <c>Permissions</c> members share a name but not an element type. Inbound, AutoMapper matches them
    /// on that name and finds no <c>TeamPermissionModel</c> to <c>TeamPermissionAssignmentEntity</c> map.
    /// </remarks>
    [Fact]
    public void A_view_whose_teams_have_permission_assignments_fails_to_import()
    {
        var permission = new TeamPermissionEntity { Id = Guid.NewGuid(), Name = "ViewView" };
        var role = new TeamRoleEntity { Id = Guid.NewGuid(), Name = "Member" };

        var team = new TeamEntity
        {
            Id = Guid.NewGuid(),
            Name = "Team",
            RoleId = role.Id,
            Role = role,
        };

        team.Permissions.Add(new TeamPermissionAssignmentEntity
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            PermissionId = permission.Id,
            Permission = permission,
        });

        var view = new ViewEntity { Id = Guid.NewGuid(), Name = "View" };
        view.Teams.Add(team);

        var export = Export(view);

        // The export does carry the permission, which is what makes the return trip a real scenario
        // rather than a hypothetical one.
        Assert.Equal("ViewView", Assert.Single(Assert.Single(export.Teams).Permissions).Name);

        var ex = Assert.Throws<AutoMapperMappingException>(
            () => TestMapper.Mapper.Map<ViewEntity>(export));

        // Assert on the member rather than just the exception type, so an unrelated mapping failure
        // elsewhere in ViewExport -> ViewEntity cannot keep this test green.
        Assert.Equal("Teams", ex.MemberMap?.DestinationName);
    }

    /// <summary>
    /// Mirrors the two hops <c>Export</c> makes — entity to <c>ViewExportDTO</c>, then to
    /// <c>ViewExport</c> — rather than mapping straight to <c>ViewExport</c>, so the test travels the
    /// same maps the endpoint does.
    /// </summary>
    private static ViewExport Export(ViewEntity view) =>
        TestMapper.Mapper.Map<ViewExport>(TestMapper.Mapper.Map<ViewExportDTO>(view));
}
