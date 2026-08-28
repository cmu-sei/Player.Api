// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NSubstitute.Core;
using Player.Api.Data.Data.Models;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Services;

/// <summary>
/// Who is online in a view. Connections live in a process-local cache rather than the database, so the
/// behaviour under test is the interaction between that cache, the hub broadcasts, and the caller's
/// primary team claim — which decides how much of the view they see.
/// </summary>
public class PresenceServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    // ---- Adding a connection ----------------------------------------------------------------------

    [Fact]
    public async Task AddConnectionToView_records_the_connection_against_the_primary_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);

        var membershipId = await Service(caller).AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);

        Assert.Equal(member.Id, membershipId);
        Assert.Equal(team.Id, Connections(caller, member.Id)["conn-1"]);
    }

    /// <summary>
    /// Both groups, because a client watching only its own team still has to see its teammates come and
    /// go.
    /// </summary>
    [Fact]
    public async Task AddConnectionToView_broadcasts_to_the_view_and_the_primary_team_groups()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team, name: "Member");
        var caller = Caller(member, view, team);

        await Service(caller).AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);

        foreach (var group in new[] { view.Id, team.Id })
        {
            var presence = Assert.Single(Updates(caller, group));
            Assert.True(presence.Online);
            Assert.Equal(member.Id, presence.Id);
            Assert.Equal("Member", presence.UserName);
            Assert.Equal(view.Id, presence.ViewId);
        }
    }

    [Fact]
    public async Task AddConnectionToView_keeps_an_earlier_connection_of_the_same_user()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);

        await Service(caller).AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);
        await Service(caller).AddConnectionToView(view.Id, member.UserId, "conn-2", Ct);

        Assert.Equal(["conn-1", "conn-2"], Connections(caller, member.Id).Keys.Order());
    }

    /// <summary>
    /// Null rather than an exception: the hub calls this on every join and a user who is not a member
    /// simply has no presence to report.
    /// </summary>
    [Fact]
    public async Task AddConnectionToView_returns_null_for_a_user_who_is_not_a_member()
    {
        var view = TestData.View();
        await Seed(view);

        Assert.Null(await Service(Root).AddConnectionToView(view.Id, Guid.NewGuid(), "conn-1", Ct));
    }

    /// <summary>
    /// Presence is reported per primary team, so a membership without one cannot be placed in a group.
    /// </summary>
    [Fact]
    public async Task AddConnectionToView_returns_null_when_the_membership_has_no_primary_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(view, team, user, viewMembership);

        Assert.Null(await Service(Root).AddConnectionToView(view.Id, user.Id, "conn-1", Ct));
        Assert.Empty(Connections(Root, viewMembership.Id));
    }

    // ---- Removing a connection --------------------------------------------------------------------

    [Fact]
    public async Task RemoveConnectionFromView_reports_the_user_offline_once_the_last_connection_goes()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);
        var service = Service(caller);

        await service.AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);
        var membershipId = await service.RemoveConnectionFromView(member.Id, member.UserId, "conn-1", Ct);

        Assert.Equal(member.Id, membershipId);
        Assert.Empty(Connections(caller, member.Id));
        Assert.False(Updates(caller, view.Id).Last().Online);
        Assert.False(Updates(caller, team.Id).Last().Online);
    }

    /// <summary>
    /// A second browser tab keeps the user online, which is why the cache stores connections rather than
    /// a flag.
    /// </summary>
    [Fact]
    public async Task RemoveConnectionFromView_keeps_the_user_online_while_another_connection_remains()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);
        var service = Service(caller);

        await service.AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);
        await service.AddConnectionToView(view.Id, member.UserId, "conn-2", Ct);
        await service.RemoveConnectionFromView(member.Id, member.UserId, "conn-1", Ct);

        Assert.True(Updates(caller, view.Id).Last().Online);
        Assert.True(Updates(caller, team.Id).Last().Online);
    }

    [Fact]
    public async Task RemoveConnectionFromView_returns_null_for_an_unknown_membership()
    {
        Assert.Null(await Service(Root).RemoveConnectionFromView(Guid.NewGuid(), Guid.NewGuid(), "conn-1", Ct));
    }

    /// <summary>
    /// Nothing was ever cached for this membership, so there is nothing to broadcast — the caller learns
    /// that from the null.
    /// </summary>
    [Fact]
    public async Task RemoveConnectionFromView_returns_null_when_the_membership_has_no_connections()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);

        Assert.Null(await Service(Root).RemoveConnectionFromView(member.Id, member.UserId, "conn-1", Ct));
    }

    /// <summary>
    /// A connection id the cache does not hold leaves the stored connections alone, and reports the view
    /// as still online because another connection is present.
    /// </summary>
    [Fact]
    public async Task RemoveConnectionFromView_ignores_an_unknown_connection_id()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);
        var service = Service(caller);

        await service.AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);
        await service.RemoveConnectionFromView(member.Id, member.UserId, "conn-2", Ct);

        Assert.Equal(["conn-1"], Connections(caller, member.Id).Keys);
        Assert.True(Updates(caller, view.Id).Last().Online);

        // Only the join broadcast to the team group: without a stored primary team there is nothing to
        // address the removal to.
        Assert.Single(Updates(caller, team.Id));
    }

    // ---- Groups -----------------------------------------------------------------------------------

    /// <summary>
    /// One group for the whole view, since a caller who can see every team does not need to subscribe to
    /// each of them.
    /// </summary>
    [Fact]
    public async Task GetGroupsByViewId_returns_the_view_group_for_a_caller_who_can_see_every_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team, TestData.Team(view.Id, "Other"));
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team, viewPermissions: [ViewPermission.ViewView]);

        Assert.Equal([$"Presence-{view.Id}"], await Service(caller).GetGroupsByViewId(view.Id));
    }

    [Fact]
    public async Task GetGroupsByViewId_returns_only_the_callers_own_team_group()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team, TestData.Team(view.Id, "Other"));
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);

        Assert.Equal([$"Presence-{team.Id}"], await Service(caller).GetGroupsByViewId(view.Id));
    }

    /// <summary>
    /// A team scoped onto the caller's primary team is visible to it, so its group is subscribed to as
    /// well.
    /// </summary>
    [Fact]
    public async Task GetGroupsByViewId_includes_a_team_scoped_onto_the_primary_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var scoped = TestData.Team(view.Id, "Scoped");
        await Seed(view, team, scoped);
        var member = await SeedMember(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithUserId(member.UserId)
            .WithTeam(view.Id, team.Id, isPrimary: true, teamPermissions: [TeamPermission.ViewTeam])
            .WithScopedTeam(view.Id, scoped.Id, sourceTeamIds: [team.Id], teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        var groups = await Service(caller).GetGroupsByViewId(view.Id);

        Assert.Equal(
            new[] { $"Presence-{team.Id}", $"Presence-{scoped.Id}" }.Order(),
            groups.Order());
    }

    /// <summary>
    /// A claim on the view that is not the primary one grants nothing: presence is reported from the
    /// team the user is acting as.
    /// </summary>
    [Fact]
    public async Task GetGroupsByViewId_returns_nothing_when_the_caller_has_no_primary_claim()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithUserId(member.UserId)
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        Assert.Empty(await Service(caller).GetGroupsByViewId(view.Id));
    }

    /// <summary>
    /// <see cref="HubException"/> rather than a domain exception, because these are only ever called
    /// from a hub and its message is what reaches the client.
    /// </summary>
    [Fact]
    public async Task GetGroupsByViewId_throws_for_a_caller_who_is_not_a_member()
    {
        var view = TestData.View();
        await Seed(view);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => Service(Root).GetGroupsByViewId(view.Id));

        Assert.Equal("The active team is not valid for this view.", exception.Message);
    }

    [Fact]
    public async Task GetGroupsByViewId_requires_an_active_team_on_the_team_overload()
    {
        var view = TestData.View();
        await Seed(view);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => Service(Root).GetGroupsByViewId(view.Id, null));

        Assert.Equal("An active team is required for presence.", exception.Message);
    }

    /// <summary>
    /// The client passes the team it believes it is acting as; a mismatch means the two have diverged and
    /// the answer would be wrong for either.
    /// </summary>
    [Fact]
    public async Task GetGroupsByViewId_throws_when_the_active_team_is_not_the_primary_one()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other");
        await Seed(view, team, other);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => Service(caller).GetGroupsByViewId(view.Id, other.Id));

        Assert.Equal("The active team is not valid for this view.", exception.Message);
    }

    [Fact]
    public async Task GetGroupsByViewId_accepts_the_primary_team_as_the_active_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team);

        Assert.Equal([$"Presence-{team.Id}"], await Service(caller).GetGroupsByViewId(view.Id, team.Id));
    }

    // ---- Presence ---------------------------------------------------------------------------------

    /// <summary>
    /// Every member of the view, online or not — a client renders the roster from this, so absent users
    /// have to appear.
    /// </summary>
    [Fact]
    public async Task GetPresenceByViewId_lists_every_member_for_a_caller_who_can_see_every_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other");
        await Seed(view, team, other);
        var member = await SeedMember(view, team, name: "Mine");
        await SeedMember(view, other, name: "Theirs");
        var caller = Caller(member, view, team, viewPermissions: [ViewPermission.ViewView]);

        var presence = await Service(caller).GetPresenceByViewId(view.Id);

        Assert.Equal(["Mine", "Theirs"], presence.Select(x => x.UserName).Order());
        Assert.All(presence, x => Assert.False(x.Online));
    }

    [Fact]
    public async Task GetPresenceByViewId_reports_a_connected_member_as_online()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);
        var caller = Caller(member, view, team, viewPermissions: [ViewPermission.ViewView]);
        var service = Service(caller);

        await service.AddConnectionToView(view.Id, member.UserId, "conn-1", Ct);

        var presence = Assert.Single(await service.GetPresenceByViewId(view.Id));
        Assert.True(presence.Online);
    }

    /// <summary>
    /// Restricted callers see the teams they have access to and nobody else, and each member is reported
    /// as being only in those teams.
    /// </summary>
    [Fact]
    public async Task GetPresenceByViewId_hides_members_of_teams_the_caller_cannot_see()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other");
        await Seed(view, team, other);
        var member = await SeedMember(view, team, name: "Mine");
        await SeedMember(view, other, name: "Theirs");
        var caller = Caller(member, view, team);

        var presence = Assert.Single(await Service(caller).GetPresenceByViewId(view.Id));

        Assert.Equal("Mine", presence.UserName);
        Assert.Equal([team.Id], presence.TeamIds);
    }

    /// <summary>
    /// A member of two teams is reported in both when the caller can see the whole view.
    /// </summary>
    [Fact]
    public async Task GetPresenceByViewId_reports_every_team_of_a_member_to_a_caller_who_sees_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var second = TestData.Team(view.Id, "Second");
        await Seed(view, team, second);
        var member = await SeedMember(view, team);
        await Seed(TestData.TeamMembership(second.Id, member.UserId, member.Id));
        var caller = Caller(member, view, team, viewPermissions: [ViewPermission.ViewView]);

        var presence = Assert.Single(await Service(caller).GetPresenceByViewId(view.Id));

        Assert.Equal(new[] { team.Id, second.Id }.Order(), presence.TeamIds.Order());
    }

    /// <summary>
    /// Online is per accessible team: a user connected as a team the caller cannot see reads as offline
    /// rather than leaking that they are present elsewhere.
    /// </summary>
    [Fact]
    public async Task GetPresenceByViewId_reports_a_member_offline_when_their_connection_is_in_a_hidden_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other");
        await Seed(view, team, other);
        var member = await SeedMember(view, team);
        var otherMember = await SeedMember(view, other, name: "Theirs");
        await Seed(TestData.TeamMembership(team.Id, otherMember.UserId, otherMember.Id));

        var caller = Caller(member, view, team);
        var service = Service(caller);

        // The other user connects as their own primary team, which this caller cannot see.
        await service.AddConnectionToView(view.Id, otherMember.UserId, "conn-1", Ct);

        var presence = await service.GetPresenceByViewId(view.Id);

        Assert.False(Assert.Single(presence, x => x.UserName == "Theirs").Online);
    }

    [Fact]
    public async Task GetPresenceByViewId_returns_nothing_when_the_caller_has_no_primary_claim()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithUserId(member.UserId)
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        Assert.Empty(await Service(caller).GetPresenceByViewId(view.Id));
    }

    [Fact]
    public async Task GetPresenceByViewId_requires_an_active_team_on_the_team_overload()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<HubException>(() => Service(Root).GetPresenceByViewId(view.Id, null));
    }

    [Fact]
    public async Task GetPresenceByViewId_accepts_the_primary_team_as_the_active_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);
        var member = await SeedMember(view, team, name: "Mine");
        var caller = Caller(member, view, team);

        var presence = await Service(caller).GetPresenceByViewId(view.Id, team.Id);

        Assert.Equal("Mine", Assert.Single(presence).UserName);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private IPresenceService Service(ClaimsPrincipal user) => HostFor(user).Resolve<IPresenceService>();

    /// <summary>
    /// A membership of <paramref name="view"/> whose primary team is <paramref name="team"/>. The
    /// primary is assigned after the insert because the two rows reference each other.
    /// </summary>
    private async Task<ViewMembershipEntity> SeedMember(
        ViewEntity view,
        TeamEntity team,
        string name = "Test User")
    {
        var user = TestData.User(name: name);
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(user, viewMembership, teamMembership);

        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        return viewMembership;
    }

    /// <summary>
    /// The principal for <paramref name="member"/>: their user id, and a primary claim on
    /// <paramref name="team"/>.
    /// </summary>
    private static ClaimsPrincipal Caller(
        ViewMembershipEntity member,
        ViewEntity view,
        TeamEntity team,
        ViewPermission[] viewPermissions = null) =>
        new ClaimsPrincipalBuilder()
            .WithUserId(member.UserId)
            .WithTeam(view.Id, team.Id, isPrimary: true, viewPermissions: viewPermissions)
            .Build();

    /// <summary>
    /// The connections cached for <paramref name="membershipId"/>. The cache is a singleton of the host,
    /// so this is the same instance the service under test wrote to.
    /// </summary>
    private Dictionary<string, Guid> Connections(ClaimsPrincipal user, Guid membershipId) =>
        HostFor(user).Resolve<ConnectionCacheService>()
            .ViewMembershipConnections.GetValueOrDefault(membershipId, []);

    /// <summary>
    /// The presence payloads broadcast to <paramref name="groupId"/>'s group, in order. Read off the
    /// substituted hub rather than matched inline, so a test can assert on the last one.
    /// </summary>
    private ViewPresence[] Updates(ClaimsPrincipal user, Guid groupId) =>
        [.. HostFor(user).ViewHub.Clients.Group($"Presence-{groupId}")
            .ReceivedCalls()
            .Where(x => x.GetMethodInfo().Name == nameof(IClientProxy.SendCoreAsync))
            .Select(x => (ViewPresence)((object[])x.GetArguments()[1])[0])];
}
