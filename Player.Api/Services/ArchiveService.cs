// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using Player.Api.Data.Models;

namespace Player.Api.Services
{
    public interface IArchiveService
    {
        Task<ArchiveResult> ArchiveData(string archiveName, ArchiveType type, Dictionary<string, object> data);
        Dictionary<string, byte[]> ExtractArchive(MemoryStream archiveStream, string archiveName);
    }

    public class ArchiveService : IArchiveService
    {
        #region Archive

        public async Task<ArchiveResult> ArchiveData(string archiveName, ArchiveType type, Dictionary<string, object> data)
        {
            var stream = new System.IO.MemoryStream();

            using (ArchiveOutputStream archiveStream = ArchiveOutputStream.Create(stream, type))
            {
                foreach (var kvp in data)
                {
                    var value = kvp.Value;
                    archiveStream.PutNextEntry(kvp.Key, GetLength(value));

                    if (value is byte[] bytes)
                    {
                        await archiveStream.Stream.WriteAsync(bytes);
                    }
                    else if (value is Stream inputStream)
                    {
                        await inputStream.CopyToAsync(archiveStream.Stream);
                    }
                    else // assume text
                    {
                        using var sw = new StreamWriter(archiveStream.Stream, leaveOpen: true);
                        await sw.WriteAsync(value.ToString());
                    }

                    archiveStream.CloseEntry();
                }
            }

            stream.Position = 0;

            return new ArchiveResult
            {
                Data = stream,
                Name = $"{archiveName}.{type.GetExtension()}",
                Type = type.GetContentType()
            };
        }

        private long GetLength(object value) =>
            value switch
            {
                byte[] bytes => bytes.Length,
                Stream stream => stream.Length,
                string str => str.Length,
                _ => value.ToString()?.Length ?? 0
            };

        #endregion

        #region Extract

        public Dictionary<string, byte[]> ExtractArchive(MemoryStream archiveStream, string archiveName)
        {
            var dict = new Dictionary<string, byte[]>();

            using (var archiveInputStream = ArchiveInputStream.Create(archiveStream, ArchiveTypeHelpers.GetType(archiveName)))
            {
                while (archiveInputStream.GetNextEntry() is ArchiveEntry archiveEntry)
                {
                    if (archiveEntry.IsFile)
                    {
                        byte[] content;

                        var buffer = new byte[4096];

                        using (var contentStream = new System.IO.MemoryStream())
                        {
                            StreamUtils.Copy(archiveInputStream.Stream, contentStream, buffer);
                            content = contentStream.ToArray();
                        }

                        dict.Add(archiveEntry.Name, content);
                    }
                }
            }

            return dict;
        }

        #endregion

        private class ArchiveOutputStream : IDisposable
        {
            public System.IO.Stream Stream { get; set; }

            private ArchiveOutputStream(System.IO.Stream stream)
            {
                this.Stream = stream;
            }

            public static ArchiveOutputStream Create(System.IO.Stream stream, ArchiveType type)
            {
                switch (type)
                {
                    case ArchiveType.zip:
                        return ArchiveOutputStream.CreateZipStream(stream);
                    case ArchiveType.tgz:
                        return ArchiveOutputStream.CreateTgzStream(stream);
                    default:
                        throw new ArgumentException();
                }
            }

            private static ArchiveOutputStream CreateZipStream(System.IO.Stream stream)
            {
                ZipOutputStream zipStream = new ZipOutputStream(stream);
                zipStream.IsStreamOwner = false;
                return new ArchiveOutputStream(zipStream);
            }

            private static ArchiveOutputStream CreateTgzStream(System.IO.Stream stream)
            {
                GZipOutputStream gzipStream = new(stream);
                TarOutputStream tarStream = new(gzipStream, Encoding.UTF8);
                gzipStream.IsStreamOwner = false;
                return new ArchiveOutputStream(tarStream);
            }

            public void PutNextEntry(string name, long size)
            {
                if (Stream is ZipOutputStream)
                {
                    ((ZipOutputStream)Stream).PutNextEntry(new ZipEntry(name));
                }
                else if (Stream is TarOutputStream)
                {
                    var tarEntry = TarEntry.CreateTarEntry(name);
                    tarEntry.Size = size;
                    ((TarOutputStream)Stream).PutNextEntry(tarEntry);
                }
            }

            public void CloseEntry()
            {
                if (Stream is TarOutputStream)
                {
                    ((TarOutputStream)Stream).CloseEntry();
                }
            }

            public void Dispose()
            {
                this.Stream.Dispose();
            }
        }

        private class ArchiveInputStream : IDisposable
        {
            public System.IO.Stream Stream { get; set; }

            private ArchiveInputStream(System.IO.Stream stream)
            {
                this.Stream = stream;
            }

            public static ArchiveInputStream Create(System.IO.Stream stream, ArchiveType type)
            {
                switch (type)
                {
                    case ArchiveType.zip:
                        return ArchiveInputStream.CreateZipStream(stream);
                    case ArchiveType.tgz:
                        return ArchiveInputStream.CreateTgzStream(stream);
                    default:
                        throw new ArgumentException();
                }
            }

            private static ArchiveInputStream CreateZipStream(System.IO.Stream stream)
            {
                ZipInputStream zipStream = new ZipInputStream(stream);
                zipStream.IsStreamOwner = false;
                return new ArchiveInputStream(zipStream);
            }

            private static ArchiveInputStream CreateTgzStream(System.IO.Stream stream)
            {
                GZipInputStream gzipStream = new(stream);
                TarInputStream tarStream = new(gzipStream, Encoding.UTF8);
                gzipStream.IsStreamOwner = false;
                return new ArchiveInputStream(tarStream);
            }

            public ArchiveEntry GetNextEntry()
            {
                ArchiveEntry archiveEntry = null;

                if (Stream is ZipInputStream)
                {
                    var zipEntry = ((ZipInputStream)Stream).GetNextEntry();

                    if (zipEntry != null)
                    {
                        archiveEntry = new ArchiveEntry(zipEntry);
                    }
                }
                else if (Stream is TarInputStream)
                {
                    var tarEntry = ((TarInputStream)Stream).GetNextEntry();

                    if (tarEntry != null)
                    {
                        archiveEntry = new ArchiveEntry(tarEntry);
                    }
                }

                return archiveEntry;
            }

            public void Dispose()
            {
                this.Stream.Dispose();
            }
        }

        private class ArchiveEntry
        {
            private ZipEntry ZipEntry { get; set; }
            private TarEntry TarEntry { get; set; }

            public ArchiveEntry(ZipEntry zipEntry)
            {
                this.ZipEntry = zipEntry;
            }

            public ArchiveEntry(TarEntry tarEntry)
            {
                this.TarEntry = tarEntry;
            }

            public bool IsFile
            {
                get
                {
                    if (this.ZipEntry != null)
                    {
                        return !this.ZipEntry.IsDirectory;
                    }

                    if (this.TarEntry != null)
                    {
                        return !this.TarEntry.IsDirectory;
                    }

                    return false;
                }
            }

            public string Name
            {
                get
                {
                    if (this.ZipEntry != null)
                    {
                        return this.ZipEntry.Name;
                    }

                    if (this.TarEntry != null)
                    {
                        return this.TarEntry.Name;
                    }

                    return null;
                }
            }
        }
    }
}
