// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Endpoints;

/// <summary>
/// What the API does with an enum value outside the set the enum defines.
/// </summary>
/// <remarks>
/// <para>
/// A name it does not recognize is refused, and a number it does not recognize is not: <c>System.Text.Json</c>
/// reads a numeric enum without checking it against the defined values, and neither the commands nor the
/// entities check afterwards. So the number is stored, returned, and — for webhook event types — used as a
/// matching key that can never match. Issue 64.
/// </para>
/// <para>
/// Both halves are here on purpose. The refusals are what make the acceptances a bug rather than a
/// convention: the same undefined value is a 400 spelled one way and a 201 spelled the other.
/// </para>
/// </remarks>
public class OutOfRangeEnumTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>
    /// A view is created with a status no <c>ViewStatus</c> member has, and the row keeps it.
    /// </summary>
    /// <remarks>
    /// Turns red when the command validates the value. Both spellings are covered because
    /// <c>JsonStringEnumConverter</c> reads a quoted number as a number, so a client cannot avoid this by
    /// sending strings. The response is asserted too: it echoes <c>999</c>, where a defined status would be
    /// a name, so the value leaks straight back out to every client that reads the view.
    /// </remarks>
    [Theory]
    [InlineData("999", 999)]
    [InlineData("\"999\"", 999)]
    [InlineData("-1", -1)]
    public async Task A_view_status_outside_the_enum_is_stored_and_returned(string status, int expected)
    {
        var response = await RootClient.PostAsync(
            "api/views", Json($"{{\"name\":\"Odd\",\"status\":{status}}}"), Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.Contains($"\"status\":{expected}", await response.Content.ReadAsStringAsync(Ct));

        await using var db = NewContext();
        Assert.Equal(expected, (int)(await db.Views.SingleAsync(x => x.Name == "Odd", Ct)).Status);
    }

    /// <summary>
    /// The contrast: a status name the enum does not define is refused before any of this, so the check the
    /// numeric path is missing does exist — just only for names.
    /// </summary>
    [Fact]
    public async Task A_view_status_name_the_enum_does_not_define_is_a_bad_request()
    {
        await AssertStatus(
            HttpStatusCode.BadRequest,
            await RootClient.PostAsync(
                "api/views", Json("{\"name\":\"Odd\",\"status\":\"Nonsense\"}"), Ct));
    }

    /// <summary>
    /// A subscription is created for an event type that does not exist, and the row is written. This is the
    /// costly one: <c>BackgroundWebhookService.cs:104</c> matches subscriptions with
    /// <c>et.EventType == evt.Type</c>, so the subscription is registered and can never fire.
    /// </summary>
    /// <remarks>
    /// Turns red when the event types are validated. A client that mistypes one is told the subscription was
    /// created, which is the answer that makes this worth more than its status code — nothing later reports
    /// that the row is unreachable.
    /// </remarks>
    [Fact]
    public async Task A_webhook_event_type_outside_the_enum_is_stored_as_a_key_that_cannot_match()
    {
        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.PostAsync(
                "api/webhooks/subscribe",
                Json("{\"name\":\"W\",\"callbackUri\":\"https://example.test/hook\",\"eventTypes\":[999]}"),
                Ct));

        await using var db = NewContext();
        var stored = await db.Webhooks.Include(x => x.EventTypes).SingleAsync(Ct);
        Assert.Equal(999, (int)Assert.Single(stored.EventTypes).EventType);
        Assert.DoesNotContain(Enum.GetValues<EventType>(), x => (int)x == 999);
    }

    /// <summary>
    /// A notification is broadcast with a priority that does not exist, and stored with it. Cheaper than the
    /// webhook case — nothing matches on priority — but the same missing check.
    /// </summary>
    [Fact]
    public async Task A_notification_priority_outside_the_enum_is_broadcast_and_stored()
    {
        var view = TestData.View();
        await Seed(view);

        await AssertStatus(
            HttpStatusCode.OK,
            await RootClient.PostAsync(
                $"api/views/{view.Id}/notifications",
                Json("{\"text\":\"hi\",\"priority\":999}"),
                Ct));

        await using var db = NewContext();
        Assert.Equal(999, (int)(await db.Notifications.SingleAsync(Ct)).Priority);
    }

    /// <summary>
    /// The query-string half, and the one place an undefined value is not merely stored: the export route
    /// binds it, then <c>ArchiveService.cs:61</c> asks it for a file extension and
    /// <c>ArchiveTypeHelpers.GetExtension</c> throws a bare <c>ArgumentException</c> for the default case —
    /// as does <c>GetContentType</c> on the next line, whichever is reached first.
    /// </summary>
    /// <remarks>
    /// Turns red when the value is validated at the edge, or when the helper throws something
    /// <c>ExceptionMiddleware</c> maps. The detail is the framework's message for an argument exception with
    /// no message of its own, which is why the 500 says nothing about archives.
    /// </remarks>
    [Fact]
    public async Task An_archive_type_outside_the_enum_is_a_server_error()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.GetAsync("api/views/actions/export?archiveType=5", Ct));

        Assert.Equal("Value does not fall within the expected range.", problem.Detail);
    }

    /// <summary>
    /// The same contrast as the view status: a name the enum does not define fails to bind, so the query
    /// string is checked for names and not for numbers.
    /// </summary>
    [Fact]
    public async Task An_archive_type_name_the_enum_does_not_define_is_a_bad_request()
    {
        var response = await RootClient.GetAsync("api/views/actions/export?archiveType=nonsense", Ct);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
    }

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
}
