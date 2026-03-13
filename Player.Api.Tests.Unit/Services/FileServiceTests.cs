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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Options;
using Player.Api.Services;
using Player.Api.Tests.Shared.Fixtures;
using Player.Api.ViewModels;
using Shouldly;
using Xunit;

namespace Player.Api.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class FileServiceTests
{
    private readonly IFixture _fixture;
    private readonly PlayerContext _context;
    private readonly IPlayerAuthorizationService _authorizationService;
    private readonly IMapper _mapper;
    private readonly ClaimsPrincipal _user;
    private readonly FileUploadOptions _fileUploadOptions;
    private readonly ITeamService _teamService;
    private readonly FileService _sut;

    public FileServiceTests()
    {
        _fixture = new Fixture().Customize(new PlayerCustomization())
                                .Customize(new AutoFakeItEasyCustomization { ConfigureMembers = true });

        _context = TestDbContextFactory.Create<PlayerContext>();

        _authorizationService = A.Fake<IPlayerAuthorizationService>();
        _mapper = A.Fake<IMapper>();
        _teamService = A.Fake<ITeamService>();

        _fileUploadOptions = new FileUploadOptions
        {
            basePath = "/tmp/player-test-files",
            maxSize = 10_000_000,
            allowedExtensions = new[] { ".txt", ".pdf", ".png", ".zip" }
        };

        var userId = Guid.NewGuid();
        _user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString())
        }, "test"));

        _sut = new FileService(_user, _authorizationService, _fileUploadOptions, _context, _mapper, _teamService);
    }

    [Fact]
    public async Task UploadAsync_WhenTeamIdsNull_ThrowsForbiddenException()
    {
        // Arrange
        var form = new FileForm
        {
            viewId = Guid.NewGuid(),
            teamIds = null!,
            ToUpload = new List<Microsoft.AspNetCore.Http.IFormFile>()
        };

        // Act & Assert
        await Should.ThrowAsync<ForbiddenException>(
            () => _sut.UploadAsync(form, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WhenUserUnauthorized_ThrowsForbiddenException()
    {
        // Arrange
        A.CallTo(() => _authorizationService.Authorize(
            A<SystemPermission[]>._,
            A<CancellationToken>._))
            .Returns(false);

        // Act & Assert
        await Should.ThrowAsync<ForbiddenException>(
            () => _sut.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WhenUserAuthorized_ReturnsFiles()
    {
        // Arrange
        A.CallTo(() => _authorizationService.Authorize(
            A<SystemPermission[]>._,
            A<CancellationToken>._))
            .Returns(true);

        var files = _fixture.CreateMany<FileEntity>(2).ToList();
        _context.Files.AddRange(files);
        _context.SaveChanges();

        var expectedModels = files.Select(f => new FileModel
        {
            id = f.Id,
            Name = f.Name,
            teamIds = f.TeamIds
        }).ToList();

        A.CallTo(() => _mapper.Map<IEnumerable<FileModel>>(A<object>._))
            .Returns(expectedModels);

        // Act
        var result = await _sut.GetAsync(CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetByIdAsync_WhenFileNotFound_ThrowsEntityNotFoundException()
    {
        // Arrange
        var fileId = Guid.NewGuid();
        // No files added to context

        // Act & Assert
        await Should.ThrowAsync<EntityNotFoundException<FileModel>>(
            () => _sut.GetByIdAsync(fileId, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WhenFileNotFound_ThrowsEntityNotFoundException()
    {
        // Arrange
        var fileId = Guid.NewGuid();
        // No files added to context

        // Act & Assert
        await Should.ThrowAsync<EntityNotFoundException<FileModel>>(
            () => _sut.DeleteAsync(fileId, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WhenUserUnauthorized_ThrowsForbiddenException()
    {
        // Arrange
        var viewId = Guid.NewGuid();
        var view = _fixture.Build<ViewEntity>().With(v => v.Id, viewId).Create();
        var file = _fixture.Build<FileEntity>()
            .With(f => f.View, view)
            .Create();

        _context.Views.Add(view);
        _context.Files.Add(file);
        _context.SaveChanges();

        A.CallTo(() => _authorizationService.Authorize<ViewEntity>(
            viewId,
            A<SystemPermission[]>._,
            A<ViewPermission[]>._,
            A<TeamPermission[]>._,
            A<CancellationToken>._))
            .Returns(false);

        // Act & Assert
        await Should.ThrowAsync<ForbiddenException>(
            () => _sut.DeleteAsync(file.Id, CancellationToken.None));
    }
}
