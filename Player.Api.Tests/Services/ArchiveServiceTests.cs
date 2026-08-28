// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using Player.Api.Data.Models;
using Player.Api.Services;

namespace Player.Api.Tests.Services;

/// <summary>
/// Covers the two archive formats view export and application-template export share, and the entry-size
/// header that only one of them reads.
/// </summary>
/// <remarks>
/// No database and no options — <see cref="ArchiveService"/> takes neither. The round-trip tests assert
/// through <c>ExtractArchive</c> rather than on bytes, because that is the pairing the import half relies on.
/// </remarks>
public class ArchiveServiceTests
{
    private readonly ArchiveService _service = new();

    /// <summary>
    /// Characterizes issue 42: <c>ArchiveService.cs:71</c> declares a text entry's tar size as
    /// <c>str.Length</c>, a char count, then writes UTF-8. Any non-ASCII character makes the two disagree and
    /// tar refuses the entry. Reached in production by application-template export, which serializes without
    /// escaping; view export escapes to ASCII first and is safe.
    /// </summary>
    [Fact]
    public async Task Tgz_rejects_a_text_entry_containing_a_non_ascii_character()
    {
        var ex = await Assert.ThrowsAsync<TarException>(() => Archive(ArchiveType.tgz, "Übung"));

        // No bytes reach the stream at all: the over-long write is refused, so CloseEntry is skipped and the
        // entry is abandoned when the archive is disposed. That secondary failure is what surfaces, which is
        // why the message reports zero of the five declared bytes rather than the six that were offered.
        Assert.Contains("Entry closed at '0' before the '5' bytes specified in the header", ex.Message);
    }

    /// <summary>Zip is never told the size, so the same content that breaks tgz round trips here.</summary>
    [Fact]
    public async Task Zip_round_trips_a_text_entry_containing_a_non_ascii_character()
    {
        Assert.Equal("Übung", await RoundTripText(ArchiveType.zip, "Übung"));
    }

    /// <summary>The tgz text path itself is sound — it is only the size that is wrong.</summary>
    [Fact]
    public async Task Tgz_round_trips_an_ascii_text_entry()
    {
        Assert.Equal("Exercise", await RoundTripText(ArchiveType.tgz, "Exercise"));
    }

    /// <summary>
    /// The byte and stream branches measure their own length, so they are unaffected — which is why an
    /// export carrying binary files still succeeds while its <c>views.json</c> is what fails.
    /// </summary>
    [Theory]
    [InlineData(ArchiveType.zip)]
    [InlineData(ArchiveType.tgz)]
    public async Task Byte_and_stream_entries_round_trip(ArchiveType type)
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x10, 0x42 };

        var entries = await RoundTrip(
            type,
            new Dictionary<string, object>
            {
                ["raw.bin"] = bytes,
                ["streamed.bin"] = new MemoryStream(bytes)
            });

        Assert.Equal(bytes, entries["raw.bin"]);
        Assert.Equal(bytes, entries["streamed.bin"]);
    }

    /// <summary>
    /// The archive's name is the only thing that decides how it is read back, which is why
    /// <c>Import.ExtractArchive</c> hands the upload's own file name straight through.
    /// </summary>
    [Theory]
    [InlineData("views.zip", ArchiveType.zip)]
    [InlineData("views.tar.gz", ArchiveType.tgz)]
    [InlineData("VIEWS.TGZ", ArchiveType.tgz)]
    public void An_archives_type_is_read_from_its_extension(string name, ArchiveType expected)
    {
        Assert.Equal(expected, ArchiveTypeHelpers.GetType(name));
    }

    /// <summary>An unrecognised extension is refused rather than guessed at.</summary>
    [Fact]
    public void An_archive_with_an_unknown_extension_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ArchiveTypeHelpers.GetType("views.rar"));
    }

    private Task<ArchiveResult> Archive(ArchiveType type, string content) =>
        _service.ArchiveData("views", type, new Dictionary<string, object> { ["views.json"] = content });

    private async Task<string> RoundTripText(ArchiveType type, string content)
    {
        var entries = await RoundTrip(type, new Dictionary<string, object> { ["views.json"] = content });
        return Encoding.UTF8.GetString(entries["views.json"]);
    }

    private async Task<Dictionary<string, byte[]>> RoundTrip(
        ArchiveType type,
        Dictionary<string, object> data)
    {
        var archive = await _service.ArchiveData("views", type, data);

        using var stream = new MemoryStream();
        archive.Data.Position = 0;
        await archive.Data.CopyToAsync(stream, Ct);
        stream.Position = 0;

        return _service.ExtractArchive(stream, archive.Name);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
