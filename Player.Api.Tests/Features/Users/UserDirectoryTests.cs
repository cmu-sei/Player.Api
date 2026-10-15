// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using MediatR;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Users;
using Player.Api.Options;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Users;

/// <summary>
/// What the user directory (<c>GET api/users</c>) returns beside each user: the identity attributes the
/// deployment configures, which only a caller holding <c>ViewUsers</c> sees.
/// </summary>
/// <remarks>
/// The hosted application configures no identity attribute, so over HTTP both kinds of caller see an
/// empty list and the difference never shows. These tests send the query through a host whose options
/// name one attribute; the authorization service and handlers in it are the real ones, and the route's
/// own gate is tested over HTTP in <see cref="UserRequestTests"/>.
/// </remarks>
public class UserDirectoryTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>The key of the one identity attribute these hosts configure.</summary>
    private const string AttributeKey = "department";

    [Fact]
    public async Task GetAll_lists_the_identity_attributes_for_a_caller_holding_only_ViewUsers()
    {
        var user = TestData.User(name: "Listed");
        await Seed(user, TestData.UserIdentityAttribute(user.Id, AttributeKey, "Research"));

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewUsers).Build();
        // Gate: Player.Api/Features/Users/Requests/GetAll.cs#GetAll.Handler.HandleRequest
        var users = await Directory(caller);

        var attribute = Assert.Single(Assert.Single(users, x => x.Id == user.Id).IdentityAttributes);
        Assert.Equal((AttributeKey, "Department", "Research"), (attribute.Key, attribute.Name, attribute.Value));
    }

    /// <summary>
    /// The other branch: <c>ManageView</c> opens the directory, and the identity attributes stay out of
    /// it.
    /// </summary>
    [Fact]
    public async Task GetAll_leaves_out_the_identity_attributes_for_a_caller_holding_only_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User(name: "Listed");
        await Seed(view, team, user, TestData.UserIdentityAttribute(user.Id, AttributeKey, "Research"));

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();
        // Gate: Player.Api/Features/Users/Requests/GetAll.cs#GetAll.Handler.HandleRequest
        var users = await Directory(caller);

        var listed = Assert.Single(users, x => x.Id == user.Id);
        Assert.Equal("Listed", listed.Name);
        Assert.Empty(listed.IdentityAttributes);
    }

    private Task<UserDirectoryEntry[]> Directory(ClaimsPrincipal caller) =>
        HostFor(caller, options => options.ClaimsTransformation.UserAttributes.Add(new UserAttributeOptions
        {
            Key = AttributeKey,
            Name = "Department",
            ClaimPath = "department"
        }))
        .Resolve<IMediator>()
        .Send(new GetAll.Query(), Ct);
}
