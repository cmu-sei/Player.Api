// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net.Http.Headers;
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

    /// <summary>
    /// The entries of the archive an HTTP response carried, by name. <paramref name="name"/> is the
    /// file name the response gave it, which is how the extractor chooses between zip and tgz.
    /// </summary>
    /// <remarks>
    /// The real <see cref="ArchiveService"/> is constructed here rather than resolved: it has no
    /// constructor dependencies, so a test reading a response body does not need a container.
    /// </remarks>
    public static Dictionary<string, byte[]> ExtractFiles(byte[] archive, string name)
    {
        using var stream = new MemoryStream(archive);

        return new ArchiveService().ExtractArchive(stream, name);
    }

    /// <summary>
    /// Reads the exported views out of the bytes a response carried.
    /// </summary>
    public static ViewExport[] ReadExportedViews(byte[] archive, string name) =>
        JsonSerializer.Deserialize<ViewExport[]>(ExtractFiles(archive, name)[ViewConstants.ExportFileName]);

    /// <summary>
    /// A multipart body whose file part is named "Archive", which is what <c>[AsParameters]</c> binds
    /// <c>Import.Command.Archive</c> from. Disposable, so scope it with <c>using</c>.
    /// </summary>
    /// <remarks>
    /// The part's file name carries the extension the importer picks zip or tgz from, so it must be the
    /// name the export gave the archive rather than an invented one.
    /// </remarks>
    public static MultipartFormDataContent AsUpload(byte[] archive, string name)
    {
        var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return new MultipartFormDataContent { { content, "Archive", name } };
    }
}
