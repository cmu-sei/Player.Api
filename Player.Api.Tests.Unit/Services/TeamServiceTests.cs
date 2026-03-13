// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using AutoMapper;
using Crucible.Common.Testing.Fixtures;
using FakeItEasy;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Features.Users;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Player.Api.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class TeamServiceTests
{
    private readonly IFixture _fixture;
    private readonly PlayerContext _context;
    private readonly IPlayerAuthorizationService _authorizationService;
    private readonly IMapper _mapper;
    private readonly ClaimsPrincipal _user;
    private readonly TeamService _sut;

    public TeamServiceTests()
    {
        _fixture = new Fixture().Customize(new PlayerCustomization())
                                .Customize(new AutoFakeItEasyCustomization { ConfigureMembers = true });

        _context = TestDbContextFactory.Create<PlayerContext>();

        _authorizationService = A.Fake<IPlayerAuthorizationService>();

        var userId = Guid.NewGuid();
        _user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString())
        }, "test"));

        // Create a real mapper with the necessary profiles
        var authService = A.Fake<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        A.CallTo(() => authService.AuthorizeAsync(
            A<ClaimsPrincipal>._,
            A<object>._,
            A<System.Collections.Generic.IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement>>._))
            .Returns(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());

        var userClaimsService = A.Fake<IUserClaimsService>();
        A.CallTo(() => userClaimsService.GetCurrentClaimsPrincipal()).Returns(_user);

        var config = new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(type =>
            {
                if (type == typeof(TeamMemberResolver))
                    return new TeamMemberResolver(authService, userClaimsService);
                if (type == typeof(PrimaryTeamResolver))
                    return new PrimaryTeamResolver(authService, userClaimsService);
                return Activator.CreateInstance(type)!;
            });

            cfg.AddProfile<Player.Api.Features.Teams.MappingProfile>();
            cfg.AddProfile<Player.Api.Features.TeamRoles.MappingProfile>();
            cfg.AddProfile<Player.Api.Features.TeamPermissions.MappingProfile>();
        });

        _mapper = config.CreateMapper();

        _sut = new TeamService(_context, _user, _authorizationService, _mapper);
    }

    [Fact]
    public async Task GetByViewIdForCurrentUserAsync_WhenViewDoesNotExist_ThrowsEntityNotFoundException()
    {
        // Arrange
        var viewId = Guid.NewGuid();
        // No views added to context

        // Act & Assert
        await Should.ThrowAsync<EntityNotFoundException<View>>(
            () => _sut.GetByViewIdForCurrentUserAsync(viewId, CancellationToken.None));
    }

    [Fact]
    public async Task GetByViewIdForCurrentUserAsync_WhenUserDoesNotExist_ThrowsEntityNotFoundException()
    {
        // Arrange
        var viewId = Guid.NewGuid();

        var view = _fixture.Build<ViewEntity>()
            .With(v => v.Id, viewId)
            .Create();

        _context.Views.Add(view);
        _context.SaveChanges();
        // No users added to context

        // Act & Assert
        await Should.ThrowAsync<EntityNotFoundException<User>>(
            () => _sut.GetByViewIdForCurrentUserAsync(viewId, CancellationToken.None));
    }

    [Fact]
    public async Task GetByViewIdForCurrentUserAsync_WhenUserAuthorized_ReturnsAllTeamsInView()
    {
        // Arrange
        var viewId = Guid.NewGuid();
        var userId = Guid.Parse(_user.FindFirst("sub")!.Value);

        var view = _fixture.Build<ViewEntity>()
            .With(v => v.Id, viewId)
            .Create();

        var teams = _fixture.Build<TeamEntity>()
            .With(t => t.ViewId, viewId)
            .With(t => t.View, view)
            .CreateMany(3)
            .ToList();

        var user = _fixture.Build<UserEntity>()
            .With(u => u.Id, userId)
            .Create();

        _context.Views.Add(view);
        _context.Users.Add(user);
        _context.Teams.AddRange(teams);
        _context.SaveChanges();

        A.CallTo(() => _authorizationService.Authorize<ViewEntity>(
            viewId,
            A<SystemPermission[]>._,
            A<ViewPermission[]>._,
            A<TeamPermission[]>._,
            A<CancellationToken>._))
            .Returns(true);

        // Act
        var result = await _sut.GetByViewIdForCurrentUserAsync(viewId, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(3);
    }
}
