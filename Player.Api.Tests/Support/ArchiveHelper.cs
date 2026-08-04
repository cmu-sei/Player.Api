// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Player.Api.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Services;

namespace Player.Api.Tests.Support;

/// <summary>
/// Bridges the export and import halves of the view archive round trip, which exchange an
/// <see cref="ArchiveResult"/> for an <see cref="IFormFile"/> at the HTTP boundary.
/// </summary>
public static class ArchiveHelper
{
    /// <summary>
    /// Presents an archive as a multipart upload would. The file name carries the extension, which is
    /// how <c>ArchiveService.ExtractArchive</c> chooses between zip and tgz.
    /// </summary>
    public static IFormFile AsFormFile(ArchiveResult archive)
    {
        archive.Data.Position = 0;

        return new FormFile(archive.Data, 0, archive.Data.Length, "archive", archive.Name)
        {
            Headers = new HeaderDictionary(),
            ContentType = archive.Type
        };
    }

    /// <summary>An upload of text content, for tests that need a file in a view before exporting it.</summary>
    public static IFormFile AsFormFile(string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name)
        {
            Headers = new HeaderDictionary()
        };
    }

    /// <summary>
    /// Reads the exported views back out, so a test can assert on an archive's contents rather than
    /// only that it produced bytes.
    /// </summary>
    public static ViewExport[] ReadExportedViews(ArchiveResult archive, IArchiveService archiveService) =>
        JsonSerializer.Deserialize<ViewExport[]>(
            ExtractFiles(archive, archiveService)[ViewConstants.ExportFileName]);

    /// <summary>
    /// The archive's entries by name, for asserting on what an export packed alongside its json.
    /// </summary>
    public static Dictionary<string, byte[]> ExtractFiles(
        ArchiveResult archive,
        IArchiveService archiveService)
    {
        archive.Data.Position = 0;

        using var stream = new MemoryStream();
        archive.Data.CopyTo(stream);
        stream.Position = 0;

        return archiveService.ExtractArchive(stream, archive.Name);
    }
}
