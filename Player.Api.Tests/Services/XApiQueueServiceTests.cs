// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Services;

/// <summary>
/// The store-and-forward queue between emitting an xAPI statement and sending it to the LRS. The status
/// column is the whole protocol: <c>Pending</c> is what the sender selects, <c>Processing</c> is a claim
/// on a row, and a transient failure has to return the row to <c>Pending</c> or the statement is lost.
/// </summary>
public class XApiQueueServiceTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    private static readonly DateTime Noon = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    // ---- Enqueueing ------------------------------------------------------------------------------

    /// <summary>
    /// The queue stamps the row itself rather than trusting the caller, so a statement always arrives in
    /// the one state the sender looks for.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_stores_the_statement_pending_and_unattempted()
    {
        var statement = TestData.QueuedStatement(
            status: XApiQueueStatus.Failed,
            queuedAt: Noon,
            retryCount: 7);

        await Service.EnqueueAsync(statement, Ct);

        var stored = Assert.Single(await NewContext().XApiQueuedStatements.ToListAsync(Ct));
        Assert.Equal(XApiQueueStatus.Pending, stored.Status);
        Assert.Equal(0, stored.RetryCount);
        Assert.NotEqual(Noon, stored.QueuedAt);
        Assert.Equal(DateTimeKind.Utc, stored.QueuedAt.Kind);
    }

    [Fact]
    public async Task EnqueueAsync_assigns_an_id_and_keeps_the_statement_body()
    {
        var statement = TestData.QueuedStatement(verb: "terminated");
        statement.StatementJson = """{"verb":{"id":"terminated"}}""";

        await Service.EnqueueAsync(statement, Ct);

        var stored = Assert.Single(await NewContext().XApiQueuedStatements.ToListAsync(Ct));
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal("""{"verb":{"id":"terminated"}}""", stored.StatementJson);
        Assert.Equal("terminated", stored.Verb);
    }

    // ---- Dequeueing ------------------------------------------------------------------------------

    /// <summary>
    /// Dequeueing claims the rows it returns: they come back as <c>Processing</c> with the attempt
    /// counted, so a second sender running concurrently cannot pick up the same statement.
    /// </summary>
    [Fact]
    public async Task DequeueAsync_claims_the_statements_it_returns()
    {
        await Seed(TestData.QueuedStatement());

        var dequeued = Assert.Single(await Service.DequeueAsync(10, Ct));

        Assert.Equal(XApiQueueStatus.Processing, dequeued.Status);
        Assert.Equal(1, dequeued.RetryCount);
        Assert.NotNull(dequeued.LastAttemptAt);

        var stored = Assert.Single(await NewContext().XApiQueuedStatements.ToListAsync(Ct));
        Assert.Equal(XApiQueueStatus.Processing, stored.Status);
        Assert.Equal(1, stored.RetryCount);
    }

    /// <summary>
    /// Only pending rows are candidates. A row already claimed, sent, or permanently failed must not be
    /// sent a second time.
    /// </summary>
    [Fact]
    public async Task DequeueAsync_ignores_statements_that_are_not_pending()
    {
        await Seed(
            TestData.QueuedStatement(XApiQueueStatus.Processing),
            TestData.QueuedStatement(XApiQueueStatus.Completed),
            TestData.QueuedStatement(XApiQueueStatus.Failed));

        Assert.Empty(await Service.DequeueAsync(10, Ct));
    }

    /// <summary>Oldest first, so a backlog drains in the order the statements happened.</summary>
    [Fact]
    public async Task DequeueAsync_returns_the_oldest_statements_first()
    {
        var newest = TestData.QueuedStatement(queuedAt: Noon.AddMinutes(2), verb: "newest");
        var oldest = TestData.QueuedStatement(queuedAt: Noon, verb: "oldest");
        var middle = TestData.QueuedStatement(queuedAt: Noon.AddMinutes(1), verb: "middle");
        await Seed(newest, oldest, middle);

        var dequeued = await Service.DequeueAsync(10, Ct);

        Assert.Equal(["oldest", "middle", "newest"], dequeued.Select(x => x.Verb));
    }

    /// <summary>
    /// The batch bounds how much one pass sends, and takes from the front of the queue rather than an
    /// arbitrary slice.
    /// </summary>
    [Fact]
    public async Task DequeueAsync_takes_no_more_than_the_batch_size()
    {
        await Seed(
            TestData.QueuedStatement(queuedAt: Noon, verb: "first"),
            TestData.QueuedStatement(queuedAt: Noon.AddMinutes(1), verb: "second"),
            TestData.QueuedStatement(queuedAt: Noon.AddMinutes(2), verb: "third"));

        var dequeued = await Service.DequeueAsync(2, Ct);

        Assert.Equal(["first", "second"], dequeued.Select(x => x.Verb));
        Assert.Equal(
            XApiQueueStatus.Pending,
            (await NewContext().XApiQueuedStatements.SingleAsync(x => x.Verb == "third", Ct)).Status);
    }

    [Fact]
    public async Task DequeueAsync_defaults_to_a_batch_of_ten()
    {
        for (var i = 0; i < 12; i++)
        {
            await Seed(TestData.QueuedStatement(queuedAt: Noon.AddSeconds(i)));
        }

        Assert.Equal(10, (await Service.DequeueAsync(ct: Ct)).Count);
    }

    /// <summary>An empty queue is the normal case for the sender's polling loop, not an error.</summary>
    [Fact]
    public async Task DequeueAsync_returns_an_empty_list_for_an_empty_queue()
    {
        Assert.Empty(await Service.DequeueAsync(10, Ct));
    }

    // ---- Completing and failing --------------------------------------------------------------------

    [Fact]
    public async Task MarkCompletedAsync_records_the_statement_as_sent()
    {
        var statement = TestData.QueuedStatement(XApiQueueStatus.Processing);
        await Seed(statement);

        await Service.MarkCompletedAsync(statement.Id, Ct);

        Assert.Equal(
            XApiQueueStatus.Completed,
            (await NewContext().XApiQueuedStatements.SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// A transient failure — the LRS being down — returns the row to the queue, which is what makes
    /// retries indefinite rather than capped.
    /// </summary>
    [Fact]
    public async Task MarkFailedAsync_returns_a_transient_failure_to_the_queue()
    {
        var statement = TestData.QueuedStatement(XApiQueueStatus.Processing, retryCount: 3);
        await Seed(statement);

        await Service.MarkFailedAsync(statement.Id, "connection refused", true, Ct);

        var stored = await NewContext().XApiQueuedStatements.SingleAsync(Ct);
        Assert.Equal(XApiQueueStatus.Pending, stored.Status);
        Assert.Equal("[TRANSIENT] connection refused", stored.ErrorMessage);
        Assert.Equal(3, stored.RetryCount);
    }

    /// <summary>
    /// A permanent failure — a statement the LRS rejects — is retired on the first attempt, because
    /// resending it would fail identically.
    /// </summary>
    [Fact]
    public async Task MarkFailedAsync_retires_a_permanent_failure()
    {
        var statement = TestData.QueuedStatement(XApiQueueStatus.Processing, retryCount: 1);
        await Seed(statement);

        await Service.MarkFailedAsync(statement.Id, "400 malformed statement", false, Ct);

        var stored = await NewContext().XApiQueuedStatements.SingleAsync(Ct);
        Assert.Equal(XApiQueueStatus.Failed, stored.Status);
        Assert.Equal("[PERMANENT] 400 malformed statement", stored.ErrorMessage);
    }

    /// <summary>
    /// The prefix is how an operator reading the column tells a retrying statement from a dead one, so
    /// it is part of the contract rather than log text.
    /// </summary>
    [Fact]
    public async Task MarkFailedAsync_overwrites_an_earlier_error_message()
    {
        var statement = TestData.QueuedStatement(XApiQueueStatus.Processing);
        statement.ErrorMessage = "[TRANSIENT] earlier";
        await Seed(statement);

        await Service.MarkFailedAsync(statement.Id, "later", false, Ct);

        Assert.Equal(
            "[PERMANENT] later",
            (await NewContext().XApiQueuedStatements.SingleAsync(Ct)).ErrorMessage);
    }

    /// <summary>
    /// The sender holds a row it read earlier, so by the time it reports an outcome the row may have been
    /// cleaned up. Both paths log and return rather than throwing into the background loop.
    /// </summary>
    [Fact]
    public async Task Marking_a_statement_that_no_longer_exists_does_nothing()
    {
        await Service.MarkCompletedAsync(Guid.NewGuid(), Ct);
        await Service.MarkFailedAsync(Guid.NewGuid(), "gone", true, Ct);

        Assert.Empty(await NewContext().XApiQueuedStatements.ToListAsync(Ct));
    }

    /// <summary>
    /// The retry count survives a transient failure and keeps climbing, which is the only record of how
    /// long a statement has been stuck.
    /// </summary>
    [Fact]
    public async Task A_transient_failure_is_dequeued_again_with_the_attempt_counted()
    {
        var statement = TestData.QueuedStatement();
        await Seed(statement);

        var first = Assert.Single(await Service.DequeueAsync(10, Ct));
        await Service.MarkFailedAsync(first.Id, "down", true, Ct);
        var second = Assert.Single(await Service.DequeueAsync(10, Ct));

        Assert.Equal(2, second.RetryCount);
    }

    // ---- Depth ------------------------------------------------------------------------------------

    /// <summary>
    /// Depth counts outstanding work — pending plus claimed — so a claimed row that is never reported on
    /// still shows as backlog.
    /// </summary>
    [Fact]
    public async Task GetQueueDepthAsync_counts_pending_and_processing_statements()
    {
        await Seed(
            TestData.QueuedStatement(XApiQueueStatus.Pending),
            TestData.QueuedStatement(XApiQueueStatus.Pending),
            TestData.QueuedStatement(XApiQueueStatus.Processing),
            TestData.QueuedStatement(XApiQueueStatus.Completed),
            TestData.QueuedStatement(XApiQueueStatus.Failed));

        Assert.Equal(3, await Service.GetQueueDepthAsync(Ct));
    }

    [Fact]
    public async Task GetQueueDepthAsync_is_zero_for_an_empty_queue()
    {
        Assert.Equal(0, await Service.GetQueueDepthAsync(Ct));
    }

    // ---- Retention --------------------------------------------------------------------------------

    /// <summary>
    /// Retention is by queued time and by status: a sent statement older than the cutoff is the only
    /// thing the completed sweep may remove.
    /// </summary>
    [Fact]
    public async Task GetOldCompletedStatementsAsync_returns_only_completed_rows_queued_before_the_cutoff()
    {
        var old = TestData.QueuedStatement(XApiQueueStatus.Completed, queuedAt: Noon.AddDays(-1));
        await Seed(
            old,
            TestData.QueuedStatement(XApiQueueStatus.Completed, queuedAt: Noon.AddDays(1)),
            TestData.QueuedStatement(XApiQueueStatus.Failed, queuedAt: Noon.AddDays(-1)),
            TestData.QueuedStatement(XApiQueueStatus.Pending, queuedAt: Noon.AddDays(-1)));

        var found = await Service.GetOldCompletedStatementsAsync(Noon, Ct);

        Assert.Equal([old.Id], found.Select(x => x.Id));
    }

    /// <summary>
    /// Failed rows are swept separately because they are kept for a different reason — diagnosis rather
    /// than history — and so can be given their own retention.
    /// </summary>
    [Fact]
    public async Task GetOldFailedStatementsAsync_returns_only_failed_rows_queued_before_the_cutoff()
    {
        var old = TestData.QueuedStatement(XApiQueueStatus.Failed, queuedAt: Noon.AddDays(-1));
        await Seed(
            old,
            TestData.QueuedStatement(XApiQueueStatus.Failed, queuedAt: Noon.AddDays(1)),
            TestData.QueuedStatement(XApiQueueStatus.Completed, queuedAt: Noon.AddDays(-1)));

        var found = await Service.GetOldFailedStatementsAsync(Noon, Ct);

        Assert.Equal([old.Id], found.Select(x => x.Id));
    }

    /// <summary>
    /// A row claimed by a sender that died stays <c>Processing</c> forever. Finding it by last attempt is
    /// how it gets released.
    /// </summary>
    [Fact]
    public async Task GetStuckProcessingStatementsAsync_returns_rows_claimed_before_the_threshold()
    {
        var stuck = TestData.QueuedStatement(
            XApiQueueStatus.Processing,
            lastAttemptAt: Noon.AddHours(-1));
        await Seed(
            stuck,
            TestData.QueuedStatement(XApiQueueStatus.Processing, lastAttemptAt: Noon.AddHours(1)),
            TestData.QueuedStatement(XApiQueueStatus.Pending, lastAttemptAt: Noon.AddHours(-1)));

        var found = await Service.GetStuckProcessingStatementsAsync(Noon, Ct);

        Assert.Equal([stuck.Id], found.Select(x => x.Id));
    }

    /// <summary>
    /// A processing row with no attempt time cannot be aged, so it is left alone rather than treated as
    /// infinitely old.
    /// </summary>
    [Fact]
    public async Task GetStuckProcessingStatementsAsync_ignores_a_row_with_no_last_attempt()
    {
        await Seed(TestData.QueuedStatement(XApiQueueStatus.Processing, lastAttemptAt: null));

        Assert.Empty(await Service.GetStuckProcessingStatementsAsync(Noon, Ct));
    }

    // ---- Deleting ---------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteStatementsAsync_removes_only_the_named_statements()
    {
        var deleted = TestData.QueuedStatement();
        var kept = TestData.QueuedStatement();
        await Seed(deleted, kept);

        await Service.DeleteStatementsAsync([deleted.Id], Ct);

        var remaining = await NewContext().XApiQueuedStatements.ToListAsync(Ct);
        Assert.Equal([kept.Id], remaining.Select(x => x.Id));
    }

    /// <summary>
    /// The retention sweep passes whatever it found, which is routinely nothing, and an id already gone
    /// is not an error.
    /// </summary>
    [Fact]
    public async Task DeleteStatementsAsync_tolerates_an_empty_list_and_unknown_ids()
    {
        var kept = TestData.QueuedStatement();
        await Seed(kept);

        await Service.DeleteStatementsAsync([], Ct);
        await Service.DeleteStatementsAsync([Guid.NewGuid()], Ct);

        Assert.Single(await NewContext().XApiQueuedStatements.ToListAsync(Ct));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// The queue takes no part in authorization — the sender runs with no user at all — so every test
    /// uses one host.
    /// </summary>
    private IXApiQueueService Service => RootHost.Resolve<IXApiQueueService>();
}
