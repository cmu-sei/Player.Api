// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure;

/// <summary>
/// What a cancelled token does to a request: whether the application passes the caller's token on to the
/// database, as the suite passes its own (xUnit1051).
/// </summary>
/// <remarks>
/// <para>
/// The token is cancelled before the call rather than during it. EF Core checks it before executing a
/// command, so a pre-cancelled token gives a decided answer, whereas cancelling mid-flight would race the
/// query: a test that sometimes cancels before the command and sometimes after would pass either way and
/// would say nothing about which happened.
/// </para>
/// <para>
/// These go through <see cref="IMediator"/> and the services rather than over HTTP, because
/// <c>HttpClient</c> honours a cancelled token on the client side and never sends the request — the
/// server would not see the token at all.
/// </para>
/// </remarks>
public class CancellationTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>
    /// The token a handler is handed when its caller gave up before the work began. Constructed directly
    /// rather than from a source, so there is nothing to dispose and nothing to time.
    /// </summary>
    private static readonly CancellationToken Cancelled = new(canceled: true);

    // ---- Honoured -----------------------------------------------------------------------------------

    /// <summary>
    /// A read whose token reaches the query: the work is abandoned when the caller has gone away.
    /// </summary>
    [Fact]
    public async Task A_cancelled_read_is_abandoned()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Send(new Player.Api.Features.Roles.GetAll.Query()));
    }

    /// <summary>
    /// A write reaching <c>SaveChangesAsync(cancellationToken)</c> is refused there, so a cancelled
    /// caller leaves nothing behind. The row is read back through a cold context because the entity is
    /// added to the change tracker before the save is attempted, and is still sitting in it afterwards.
    /// </summary>
    [Fact]
    public async Task A_cancelled_write_is_not_saved()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Send(new Player.Api.Features.Roles.Create.Command { Name = "Auditor" }));

        await using var db = NewContext();
        Assert.False(await db.Roles.AnyAsync(x => x.Name == "Auditor", Ct));
    }

    // ---- Not honoured -------------------------------------------------------------------------------

    /// <summary>A permission edit sent with a cancelled token is committed.</summary>
    [Fact]
    public async Task A_cancelled_permission_edit_is_committed_anyway()
    {
        await Send(new Player.Api.Features.Permissions.Edit.Command
        {
            Id = TestData.Permissions.ViewNetworks,
            Name = "Renamed"
        });

        await using var db = NewContext();
        var stored = await db.Permissions.SingleAsync(x => x.Id == TestData.Permissions.ViewNetworks, Ct);
        Assert.Equal("Renamed", stored.Name);
    }

    /// <summary>
    /// The other write in the same feature folder provides the contrast that makes this a convention
    /// problem rather than one missed argument: its two reads take no token
    /// (<c>AddToRole.cs:63</c>, <c>:67</c>) so they run for a caller that has gone, but its save does
    /// (<c>:77</c>), so the grant is refused at the last step. Two handlers written against the same base
    /// class disagree about whether a cancelled request may change anything.
    /// </summary>
    [Fact]
    public async Task A_cancelled_permission_grant_is_refused_at_the_save()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Send(new Player.Api.Features.Permissions.AddToRole.Command
            {
                RoleId = TestData.Roles.ContentDeveloper,
                PermissionId = TestData.Permissions.ViewNetworks
            }));

        await using var db = NewContext();
        Assert.False(await db.RolePermissions.AnyAsync(
            x => x.RoleId == TestData.Roles.ContentDeveloper &&
                 x.PermissionId == TestData.Permissions.ViewNetworks,
            Ct));
    }

    /// <summary>A file listing requested with a cancelled token is served.</summary>
    [Fact]
    public async Task A_cancelled_file_listing_is_served_anyway()
    {
        var view = TestData.View();
        await Seed(view, TestData.File(view, "notes.txt", "/nowhere/notes.txt"));

        var files = await RootHost.Resolve<IFileService>().GetAsync(Cancelled);

        Assert.Equal("notes.txt", Assert.Single(files).Name);
    }

    /// <summary>A view notification listing requested with a cancelled token is served.</summary>
    [Fact]
    public async Task A_cancelled_notification_listing_is_served_anyway()
    {
        var view = TestData.View();
        await Seed(view, TestData.Notification(view.Id, text: "Still delivered"));

        var notifications = await RootHost.Resolve<INotificationService>()
            .GetAllViewNotificationsAsync(view.Id, Cancelled);

        Assert.Equal("Still delivered", Assert.Single(notifications).Text);
    }

    /// <summary>
    /// Sends <paramref name="request"/> through the real handler pipeline as <c>Root</c>, with a token
    /// that is already cancelled.
    /// </summary>
    private Task Send(IRequest request) => RootHost.Resolve<IMediator>().Send(request, Cancelled);

    private Task<TResponse> Send<TResponse>(IRequest<TResponse> request) =>
        RootHost.Resolve<IMediator>().Send(request, Cancelled);
}
