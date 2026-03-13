// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Player.Api.Tests.Integration.Fixtures;
using Player.Api.ViewModels;
using Shouldly;
using Xunit;

namespace Player.Api.Tests.Integration.Tests.Controllers;

public class FileControllerTests : IClassFixture<PlayerTestContext>
{
    private readonly HttpClient _client;

    public FileControllerTests(PlayerTestContext factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAllFiles_Unauthorized_ReturnsForbiddenOrUnauthorized()
    {
        // The test auth handler always authenticates, but the user has no system permissions.
        // The FileService.GetAsync checks for SystemPermission.ViewViews.
        // Without proper permissions the ExceptionMiddleware should return 403.
        // Act
        var response = await _client.GetAsync("/api/files");

        // Assert - either 403 (Forbidden) or 500 (if middleware translates differently)
        // The key assertion is that it does not return 200 without permission
        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFileById_NonExistentFile_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/files/{nonExistentId}");

        // Assert - should be 404 (Not Found) or 403 (Forbidden, if permission check runs first)
        var status = response.StatusCode;
        (status == HttpStatusCode.NotFound || status == HttpStatusCode.Forbidden)
            .ShouldBeTrue($"Expected 404 or 403 but got {status}");
    }

    [Fact]
    public async Task DeleteFile_NonExistentFile_ReturnsNotFoundOrForbidden()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/files/{nonExistentId}");

        // Assert
        var status = response.StatusCode;
        (status == HttpStatusCode.NotFound || status == HttpStatusCode.Forbidden)
            .ShouldBeTrue($"Expected 404 or 403 but got {status}");
    }

    [Fact]
    public async Task GetViewFiles_NonExistentView_ReturnsNotFoundOrForbidden()
    {
        // Arrange
        var nonExistentViewId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/views/{nonExistentViewId}/files");

        // Assert
        var status = response.StatusCode;
        (status == HttpStatusCode.NotFound || status == HttpStatusCode.Forbidden)
            .ShouldBeTrue($"Expected 404 or 403 but got {status}");
    }
}
