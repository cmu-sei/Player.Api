// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Options;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Services;

/// <summary>
/// The loop that drains the xAPI queue to the LRS. Its two jobs are deciding whether a failed send is
/// worth retrying — the difference between a statement that survives an LRS outage and one that is
/// discarded — and pruning the queue table so it does not grow without bound.
/// </summary>
/// <remarks>
/// The service is a <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> whose work is all
/// private, so tests drive it the way the host does: <c>StartAsync</c>, wait for the effect, then
/// <c>StopAsync</c>. <see cref="Configured"/> sets a processing delay long enough that the loop cannot
/// come round a second time, which makes one start equal exactly one pass.
/// </remarks>
public class XApiBackgroundServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private const string Endpoint = "https://lrs.test";
    private const string StatementsUri = $"{Endpoint}/statements";
    private const string Username = "lrs-user";
    private const string Password = "lrs-secret";

    // ---- Sending ---------------------------------------------------------------------------------

    /// <summary>
    /// The queued JSON is posted verbatim to the LRS statements resource, with the credentials and the
    /// xAPI version header the spec requires — without either, an LRS rejects the request.
    /// </summary>
    [Fact]
    public async Task A_pending_statement_is_posted_to_the_lrs_and_marked_completed()
    {
        var (host, lrs) = Configured();
        lrs.RespondWithStatus(StatementsUri, HttpStatusCode.OK);
        var statement = Pending();
        statement.StatementJson = """{"verb":{"id":"viewed"}}""";
        await Seed(statement);

        await RunOnce(host, Reached(statement, XApiQueueStatus.Completed), "the statement to be sent");

        var sent = Assert.Single(lrs.Sent);
        Assert.Equal(StatementsUri, sent.Uri);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("""{"verb":{"id":"viewed"}}""", sent.Body);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal($"{Username}:{Password}", BasicCredentials(sent));
        Assert.Equal("1.0.3", sent.Headers["X-Experience-API-Version"]);
    }

    /// <summary>
    /// How the LRS's answer is classified is the whole retry policy. The codes that mean "try later" put
    /// the statement back on the queue; the four that mean "this statement will never be accepted" retire
    /// it. A 409 is the LRS saying it already has the statement, which is success. Anything unrecognised
    /// is treated as transient, so an unexpected answer costs a retry rather than the statement.
    /// </summary>
    [Theory]
    [InlineData(200, XApiQueueStatus.Completed)]
    [InlineData(204, XApiQueueStatus.Completed)]
    [InlineData(409, XApiQueueStatus.Completed)]
    [InlineData(429, XApiQueueStatus.Pending)]
    [InlineData(500, XApiQueueStatus.Pending)]
    [InlineData(502, XApiQueueStatus.Pending)]
    [InlineData(503, XApiQueueStatus.Pending)]
    [InlineData(504, XApiQueueStatus.Pending)]
    [InlineData(404, XApiQueueStatus.Pending)]
    [InlineData(418, XApiQueueStatus.Pending)]
    [InlineData(400, XApiQueueStatus.Failed)]
    [InlineData(401, XApiQueueStatus.Failed)]
    [InlineData(403, XApiQueueStatus.Failed)]
    [InlineData(422, XApiQueueStatus.Failed)]
    public async Task The_lrs_response_decides_whether_the_statement_is_retried(
        int status,
        XApiQueueStatus expected)
    {
        var (host, lrs) = Configured();
        lrs.RespondWithStatus(StatementsUri, (HttpStatusCode)status);
        var statement = Pending();
        await Seed(statement);

        await RunOnce(host, Reached(statement, expected), $"the statement to reach {expected}");

        Assert.Single(lrs.Sent);
    }

    /// <summary>
    /// A rejected statement records what the LRS said, prefixed so an operator can tell a dead statement
    /// from a retrying one by reading the column alone.
    /// </summary>
    [Fact]
    public async Task A_rejected_statement_records_the_status_and_the_response_body()
    {
        var (host, lrs) = Configured();
        lrs.Respond(StatementsUri, Encoding.UTF8.GetBytes("malformed statement"), "text/plain", HttpStatusCode.BadRequest);
        var statement = Pending();
        await Seed(statement);

        await RunOnce(host, Reached(statement, XApiQueueStatus.Failed), "the statement to fail");

        Assert.Equal(
            "[PERMANENT] HTTP BadRequest: malformed statement",
            (await Read(statement)).ErrorMessage);
    }

    /// <summary>
    /// An LRS that cannot be reached at all is the case retrying exists for, so a transport failure keeps
    /// the statement rather than discarding it.
    /// </summary>
    [Fact]
    public async Task A_transport_failure_keeps_the_statement_for_a_retry()
    {
        var (host, lrs) = Configured();
        lrs.RespondByThrowing(StatementsUri, new HttpRequestException("connection refused"));
        var statement = Pending();
        await Seed(statement);

        await RunOnce(host, Reached(statement, XApiQueueStatus.Pending), "the statement to be returned to the queue");

        var stored = await Read(statement);
        Assert.StartsWith("[TRANSIENT]", stored.ErrorMessage);
        Assert.Contains("connection refused", stored.ErrorMessage);
        Assert.Equal(1, stored.RetryCount);
    }

    /// <summary>
    /// A pass sends at most ten, oldest first, so a large backlog drains in order over several passes
    /// instead of one pass holding the LRS open.
    /// </summary>
    /// <remarks>
    /// <c>BatchSize</c> is a private constant (<c>XApiBackgroundService.cs:21</c>) and these cases are its
    /// boundary: one under it, exactly it — which has to leave *nothing* behind, the off-by-one a
    /// <c>Take(BatchSize - 1)</c> would produce — and one over. Twelve stays because it is where "at most
    /// ten" and "the ten oldest" are both legible at once. What is left over is named by body rather than
    /// counted, so a pass that took the newest ten fails here rather than passing on arithmetic.
    /// </remarks>
    [Theory]
    [InlineData(9, 9, 0)]
    [InlineData(10, 10, 0)]
    [InlineData(11, 10, 1)]
    [InlineData(12, 10, 2)]
    public async Task A_pass_sends_the_ten_oldest_statements(int queued, int sent, int left)
    {
        var (host, lrs) = Configured();
        lrs.RespondWithStatus(StatementsUri, HttpStatusCode.OK);

        await Seed([.. Backlog(queued)]);

        await RunOnce(
            host,
            async db => await db.XApiQueuedStatements.AsNoTracking()
                .CountAsync(x => x.Status == XApiQueueStatus.Completed, Ct) == sent,
            $"{sent} statements to be sent");

        Assert.Equal([.. Enumerable.Range(0, sent).Select(Body)], lrs.Sent.Select(x => x.Body));

        var untouched = (await Stored())
            .Where(x => x.Status == XApiQueueStatus.Pending)
            .OrderBy(x => x.QueuedAt)
            .ToList();

        Assert.Equal([.. Enumerable.Range(sent, left).Select(Body)], untouched.Select(x => x.StatementJson));
        Assert.All(untouched, x => Assert.Equal(0, x.RetryCount));
    }

    // ---- The configuration switch ------------------------------------------------------------------

    /// <summary>
    /// The loop is never entered without a username, so an unconfigured deployment runs no queries and
    /// leaves anything already queued alone. Note the check is on the username only — unlike
    /// <c>XApiService</c>, this service ignores <c>Enabled</c>.
    /// </summary>
    [Fact]
    public async Task Nothing_is_sent_when_no_username_is_configured()
    {
        var (host, lrs) = Configured(o => o.Username = null);
        var statement = Pending();
        await Seed(statement);

        var service = ActivatorUtilities.CreateInstance<XApiBackgroundService>(host.Services);
        await service.StartAsync(Ct);
        await service.StopAsync(Ct);

        Assert.Empty(lrs.Sent);
        Assert.Equal(XApiQueueStatus.Pending, (await Read(statement)).Status);
        Assert.Equal(0, (await Read(statement)).RetryCount);
    }

    /// <summary>
    /// The switch is the username even when the feature is disabled, so statements left in the queue by a
    /// previously enabled deployment are still drained.
    /// </summary>
    [Fact]
    public async Task Statements_are_still_sent_when_the_feature_flag_is_off()
    {
        var (host, lrs) = Configured(o => o.Enabled = false);
        lrs.RespondWithStatus(StatementsUri, HttpStatusCode.OK);
        var statement = Pending();
        await Seed(statement);

        await RunOnce(host, Reached(statement, XApiQueueStatus.Completed), "the statement to be sent");

        Assert.Single(lrs.Sent);
    }

    /// <summary>
    /// A delay of zero would spin the loop, so it falls back to the default rather than being taken
    /// literally.
    /// </summary>
    [Fact]
    public async Task A_processing_delay_of_zero_falls_back_to_the_default()
    {
        var (host, lrs) = Configured(o => o.ProcessingDelaySeconds = 0);
        lrs.RespondWithStatus(StatementsUri, HttpStatusCode.OK);
        var statement = Pending();
        await Seed(statement);

        await RunOnce(host, Reached(statement, XApiQueueStatus.Completed), "the statement to be sent");

        Assert.Single(lrs.Sent);
    }

    /// <summary>
    /// An empty queue costs one query and no request — the loop runs on a timer, so this is the common
    /// case.
    /// </summary>
    [Fact]
    public async Task An_empty_queue_sends_nothing()
    {
        var (host, lrs) = Configured();
        // Seeded old and already sent, so the cleanup pass that follows the send pass deletes it. That
        // deletion is the signal the pass finished, since a queue with nothing to send leaves no trace.
        var old = TestData.QueuedStatement(XApiQueueStatus.Completed, queuedAt: DateTime.UtcNow.AddDays(-30));
        await Seed(old);

        await RunOnce(
            host,
            async db => !await db.XApiQueuedStatements.AsNoTracking().AnyAsync(Ct),
            "the queue to be swept");

        Assert.Empty(lrs.Sent);
    }

    // ---- Cleanup ---------------------------------------------------------------------------------

    /// <summary>
    /// Retention is what keeps the queue table bounded: sent and permanently failed rows older than the
    /// configured window go, and anything inside the window stays for diagnosis.
    /// </summary>
    [Fact]
    public async Task Cleanup_deletes_sent_and_failed_statements_past_the_retention_window()
    {
        var (host, _) = Configured(o => o.RetentionDays = 1);
        var oldCompleted = TestData.QueuedStatement(XApiQueueStatus.Completed, queuedAt: DateTime.UtcNow.AddDays(-2));
        var oldFailed = TestData.QueuedStatement(XApiQueueStatus.Failed, queuedAt: DateTime.UtcNow.AddDays(-2));
        var recentCompleted = TestData.QueuedStatement(XApiQueueStatus.Completed, queuedAt: DateTime.UtcNow);
        await Seed(oldCompleted, oldFailed, recentCompleted);

        await RunOnce(
            host,
            async db => await db.XApiQueuedStatements.AsNoTracking().CountAsync(Ct) == 1,
            "the old statements to be deleted");

        var remaining = await Stored();
        Assert.Equal([recentCompleted.Id], remaining.Select(x => x.Id));
    }

    /// <summary>
    /// A row is marked <c>Processing</c> before it is sent, so a process that dies mid-batch leaves it
    /// there. Characterizes issue 29: the sweep <em>deletes</em> such a row rather than returning it to
    /// the queue, so the statement is lost. Flip this to expect a pending row when that is fixed.
    /// </summary>
    [Fact]
    public async Task Cleanup_deletes_a_statement_left_processing_past_the_timeout()
    {
        var (host, _) = Configured(o => o.ProcessingTimeoutMinutes = 5);
        var stuck = TestData.QueuedStatement(
            XApiQueueStatus.Processing,
            queuedAt: DateTime.UtcNow,
            retryCount: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-10));
        var inFlight = TestData.QueuedStatement(
            XApiQueueStatus.Processing,
            queuedAt: DateTime.UtcNow,
            retryCount: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-1));
        await Seed(stuck, inFlight);

        await RunOnce(
            host,
            async db => await db.XApiQueuedStatements.AsNoTracking().CountAsync(Ct) == 1,
            "the stuck statement to be deleted");

        var remaining = await Stored();
        Assert.Equal([inFlight.Id], remaining.Select(x => x.Id));
    }

    /// <summary>
    /// A statement that has just been sent is deleted in the same pass when it was queued outside the
    /// retention window — the sweep keys off queued time, not sent time.
    /// </summary>
    [Fact]
    public async Task A_statement_queued_before_the_retention_window_is_deleted_as_soon_as_it_is_sent()
    {
        var (host, lrs) = Configured(o => o.RetentionDays = 1);
        lrs.RespondWithStatus(StatementsUri, HttpStatusCode.OK);
        var statement = Pending(DateTime.UtcNow.AddDays(-30));
        await Seed(statement);

        await RunOnce(
            host,
            async db => !await db.XApiQueuedStatements.AsNoTracking().AnyAsync(Ct),
            "the statement to be sent and swept");

        Assert.Single(lrs.Sent);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// A host with the LRS configured and its HTTP client pointed at a stub.
    /// <c>ProcessingDelaySeconds</c> is long enough that the loop runs its body exactly once per start,
    /// which is what makes assertions about retry counts stable.
    /// </summary>
    private (ApiTestHost Host, StubHttpMessageHandler Lrs) Configured(Action<XApiOptions> configure = null)
    {
        var host = HostFor(new ClaimsPrincipalBuilder().Build(), o =>
        {
            o.XApi.Enabled = true;
            o.XApi.Username = Username;
            o.XApi.Password = Password;
            o.XApi.Endpoint = Endpoint;
            o.XApi.ProcessingDelaySeconds = 600;
            configure?.Invoke(o.XApi);
        });

        var lrs = new StubHttpMessageHandler();
        // A client per call, because the service adds its headers to whatever it is handed and adding the
        // same header twice throws.
        host.Resolve<IHttpClientFactory>().CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(lrs));

        return (host, lrs);
    }

    /// <summary>
    /// Runs one pass of the loop, waiting for <paramref name="until"/> to hold before stopping. The wait
    /// reads through its own context: the service writes on the context the test shares with it, and two
    /// threads may not use one <see cref="PlayerContext"/> at once.
    /// </summary>
    private async Task RunOnce(ApiTestHost host, Func<PlayerContext, Task<bool>> until, string what)
    {
        var service = ActivatorUtilities.CreateInstance<XApiBackgroundService>(host.Services);
        await service.StartAsync(Ct);

        try
        {
            await using var probe = NewContext();
            await WaitUntil(() => until(probe), what);
        }
        finally
        {
            await service.StopAsync(Ct);
        }
    }

    /// <summary>
    /// Holds once <paramref name="statement"/> has been attempted and its outcome recorded. The retry
    /// count rises in the same save that claims the row, so pairing it with a settled status is what
    /// distinguishes "reported on" from "claimed but still in flight".
    /// </summary>
    private static Func<PlayerContext, Task<bool>> Reached(
        XApiQueuedStatementEntity statement,
        XApiQueueStatus status) =>
        db => db.XApiQueuedStatements.AsNoTracking().AnyAsync(
            x => x.Id == statement.Id && x.Status == status && x.RetryCount == 1,
            Ct);

    /// <summary>
    /// A statement waiting to be sent. Queued now unless told otherwise, so the retention sweep that
    /// follows every send pass leaves it alone.
    /// </summary>
    private static XApiQueuedStatementEntity Pending(DateTime? queuedAt = null) =>
        TestData.QueuedStatement(queuedAt: queuedAt ?? DateTime.UtcNow);

    /// <summary>
    /// A backlog of <paramref name="count"/> pending statements, oldest first, each carrying its own index
    /// as its body so that what the LRS received says which statements were taken and in what order.
    /// </summary>
    private static List<XApiQueuedStatementEntity> Backlog(int count) =>
        [.. Enumerable.Range(0, count).Select(i =>
        {
            var statement = Pending(DateTime.UtcNow.AddSeconds(i - 100));
            statement.StatementJson = Body(i);

            return statement;
        })];

    /// <summary>The body of the <paramref name="n"/>th statement of a <see cref="Backlog"/>.</summary>
    private static string Body(int n) => $$"""{"n":{{n}}}""";

    private async Task<XApiQueuedStatementEntity> Read(XApiQueuedStatementEntity statement)
    {
        await using var db = NewContext();

        return await db.XApiQueuedStatements.AsNoTracking().SingleAsync(x => x.Id == statement.Id, Ct);
    }

    /// <summary>
    /// The queue as it is on disk, read through a context that lives only for the read: the service
    /// writes through the context the test holds, and an undisposed context keeps its pooled connection
    /// for the rest of the run.
    /// </summary>
    private async Task<List<XApiQueuedStatementEntity>> Stored()
    {
        await using var db = NewContext();

        return await db.XApiQueuedStatements.AsNoTracking().ToListAsync(Ct);
    }

    /// <summary>The credentials the LRS would decode, rather than the base64 the code produced.</summary>
    private static string BasicCredentials(SentRequest request) =>
        Encoding.ASCII.GetString(Convert.FromBase64String(request.Headers["Authorization"]["Basic ".Length..]));
}
