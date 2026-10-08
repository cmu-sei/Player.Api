// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Data.Data.Models;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Hubs;

/// <summary>
/// The three hubs' own <c>[Authorize(AuthenticationSchemes = "Bearer")]</c>, over real SignalR connections to
/// the in-process server at the paths <c>Startup.Configure</c> maps them to.
/// </summary>
/// <remarks>
/// <para>
/// A connection goes over WebSockets with negotiation skipped, so the upgrade request carries the actor's
/// identity headers and the test's <c>X-Test-Session</c> header, and every invocation on the connection runs
/// inside that request: the real claims transformer derives the caller's claims from the seeded rows, and the
/// hub's services read the test's own database. Each allowed case invokes <c>Join</c> and reads the hub's
/// <c>Reply</c>, which only a connection that authenticated and reached the real <c>NotificationService</c>
/// over the test's database can produce. For <c>ViewHub</c> and <c>UserHub</c> the reply's success also
/// rests on the seeded membership.
/// </para>
/// <para>
/// The hubs' methods themselves are covered with <c>HubHarness</c> in each hub's own test class.
/// </para>
/// </remarks>
public class HubConnectionTests(DatabaseFixture fixture, PlayerAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string ViewHubPath = "/hubs/view";

    private const string TeamHubPath = "/hubs/team";

    private const string UserHubPath = "/hubs/user";

    /// <summary>How long a test waits for the hub's reply before failing, rather than hanging the run.</summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    // ---- ViewHub ----------------------------------------------------------------------------------

    [Fact]
    public async Task ViewHub_lets_an_actor_join_a_view_it_is_a_member_of()
    {
        var view = TestData.View("Joined View");
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);
        var actor = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ViewView]).SeedAsync();

        var reply = await JoinAndReadReply(actor, ViewHubPath, view.Id.ToString());

        Assert.True(reply.WasSuccess);
        Assert.Equal("Joined View", reply.ToName);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task ViewHub_negotiate_without_an_identity_is_unauthorized()
    {
        await AssertStatus(
            HttpStatusCode.Unauthorized,
            await Client().PostAsync($"{ViewHubPath}/negotiate?negotiateVersion=1", null, Ct));
    }

    // ---- TeamHub ----------------------------------------------------------------------------------

    [Fact]
    public async Task TeamHub_lets_an_actor_join_a_team_it_is_on()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Joined Team", role.Id);
        await Seed(view, role, team);
        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var reply = await JoinAndReadReply(actor, TeamHubPath, team.Id.ToString());

        Assert.True(reply.WasSuccess);
        Assert.Equal("Successfully joined Joined Team notifications.", reply.Text);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task TeamHub_negotiate_without_an_identity_is_unauthorized()
    {
        await AssertStatus(
            HttpStatusCode.Unauthorized,
            await Client().PostAsync($"{TeamHubPath}/negotiate?negotiateVersion=1", null, Ct));
    }

    // ---- UserHub ----------------------------------------------------------------------------------

    /// <summary>A member of the view joins its own conversation there, which takes no view permission.</summary>
    [Fact]
    public async Task UserHub_lets_an_actor_join_its_own_conversation_in_a_view_it_is_a_member_of()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);
        var actor = await Actor().OnTeam(team, teamPermissions: [TeamPermission.ViewTeam]).SeedAsync();

        var reply = await JoinAndReadReply(actor, UserHubPath, view.Id.ToString(), actor.Id.ToString());

        Assert.True(reply.WasSuccess);
        Assert.False(reply.CanPost);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task UserHub_negotiate_without_an_identity_is_unauthorized()
    {
        await AssertStatus(
            HttpStatusCode.Unauthorized,
            await Client().PostAsync($"{UserHubPath}/negotiate?negotiateVersion=1", null, Ct));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// Connects <paramref name="actor"/> to the hub at <paramref name="path"/>, invokes <c>Join</c> with
    /// <paramref name="arguments"/>, and returns the first <c>Reply</c> the hub sends the caller.
    /// </summary>
    private async Task<Notification> JoinAndReadReply(TestActor actor, string path, params object[] arguments)
    {
        var reply = new TaskCompletionSource<Notification>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = Connection(actor, path);
        using var subscription = connection.On<Notification>("Reply", x => reply.TrySetResult(x));

        await connection.StartAsync(Ct);
        await connection.InvokeCoreAsync("Join", arguments, Ct);

        return await reply.Task.WaitAsync(ReplyTimeout, Ct);
    }

    /// <summary>
    /// A WebSocket to the <c>TestServer</c>, carrying the headers every <c>ApiTestBase</c> client sends. The
    /// JSON protocol reads enums as strings, as <c>Startup</c>'s <c>AddJsonProtocol</c> writes them.
    /// </summary>
    private HubConnection Connection(TestActor actor, string path)
    {
        var session = Client().DefaultRequestHeaders.GetValues(TestDatabaseScope.HeaderName).Single();

        return new HubConnectionBuilder()
            .WithUrl($"http://localhost{path}", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) =>
                {
                    var client = Factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers[TestAuthHandler.UserHeader] = actor.Id.ToString();
                        request.Headers[TestAuthHandler.NameHeader] = actor.Name;
                        request.Headers[TestDatabaseScope.HeaderName] = session;
                    };

                    return await client.ConnectAsync(context.Uri, ct);
                };
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
    }
}
