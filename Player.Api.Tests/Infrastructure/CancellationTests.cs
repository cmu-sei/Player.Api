// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Services;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure;

/// <summary>
/// What a cancelled token does to a request. The build makes the suite pass its own token to every awaited
/// call it can (xUnit1051), and nothing before this file asked whether the application does the same. It
/// often does not: thirty-four database calls across fourteen production files take no token, so a caller
/// that has gone away is still queried for — and in one handler here, still written for.
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
    /// The shape the rest of the API should have: the token reaches the query, so the work is abandoned
    /// rather than done for nobody.
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

    /// <summary>
    /// Characterizes issue 62. Neither the read nor the save in this handler takes the token
    /// (<c>Features/Permissions/Requests/Edit.cs:61</c>, <c>:70</c>), and <c>Authorize</c> never looks at
    /// it for a caller holding the system permission, so a cancelled edit is applied and committed. The
    /// caller is gone by then and cannot be told either way, which is the part that matters: whether the
    /// rename happened is not answerable from either end.
    /// </summary>
    /// <remarks>Turns red when the handler passes its token to the read and the save.</remarks>
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
    /// The other write in the same feature folder, and the contrast that makes issue 62 a convention
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

    /// <summary>
    /// Characterizes issue 62 on the read side. <c>GetAsync</c> takes a token and hands it to the
    /// permission check, then queries without it (<c>Services/FileService.cs:106-108</c>). The check does
    /// not look at the token for a caller holding <c>ViewViews</c>, so the whole file table is read and
    /// mapped for a caller that is no longer there.
    /// </summary>
    /// <remarks>Turns red when the query takes the token.</remarks>
    [Fact]
    public async Task A_cancelled_file_listing_is_served_anyway()
    {
        var view = TestData.View();
        await Seed(view, new FileEntity
        {
            Id = Guid.NewGuid(),
            Name = "notes.txt",
            Path = "/nowhere/notes.txt",
            View = view
        });

        var files = await RootHost.Resolve<IFileService>().GetAsync(Cancelled);

        Assert.Equal("notes.txt", Assert.Single(files).Name);
    }

    /// <summary>
    /// Characterizes issue 62 in the service that ignores the token in five separate reads
    /// (<c>Services/NotificationService.cs:75</c>, <c>:84</c>, <c>:95</c>, <c>:106</c>, <c>:362</c>). This
    /// one is also the least guarded: it runs no permission check at all, so the token is the only thing
    /// that could have stopped it.
    /// </summary>
    /// <remarks>Turns red when the query takes the token.</remarks>
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
