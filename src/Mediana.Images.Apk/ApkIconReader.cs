// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.IO;

namespace Mediana.Images.Apk
{
    /// <summary>Reads the launcher icon declared by the application in a compiled Android package.</summary>
    public static class ApkIconReader
    {
        /// <summary>Returns an owned square sRGB RGBA32 frame, or null if the application omits its icon.
        /// The caller retains the APK stream. Adaptive icons use the central launcher viewport without a device mask.</summary>
        public static async ValueTask<VideoFrame?> ReadAsync(Stream apk, int size, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(apk);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 4096);
            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream? copy = apk.CanSeek ? null : new(await BoundedStreamReader.ReadAsync(apk, 256 << 20, cancellationToken).ConfigureAwait(false), writable: false);
            await using ZipArchive archive = await ZipArchive.CreateAsync(copy ?? apk, ZipArchiveMode.Read, true, null, cancellationToken).ConfigureAwait(false);
            byte[] manifestBytes = await ReadEntryAsync(archive, "AndroidManifest.xml", 8 << 20, cancellationToken).ConfigureAwait(false);
            AndroidElement manifest = AndroidElement.Parse(manifestBytes, cancellationToken);
            if (manifest.Name != "manifest") { throw ResourceChunk.Invalid(); }
            AndroidElement? app = manifest.Child("application");
            if (app is null || !app.Attributes.TryGetValue("icon", out ResourceValue icon)) { return null; }

            byte[] tableBytes = await ReadEntryAsync(archive, "resources.arsc", 64 << 20, cancellationToken).ConfigureAwait(false);
            AndroidResourceTable resources = new(tableBytes, size, cancellationToken);
            MediaBufferAllocator allocator = new(256L << 20);
            using PixelBuffer output = new(size, size, allocator);
            ApkDrawableRenderer renderer = new(archive, resources, new RasterCanvas(output), allocator, cancellationToken);
            await renderer.DrawAsync(icon, false).ConfigureAwait(false);
            return output.PublishVideoFrame(cancellationToken);
        }

        internal static async ValueTask<byte[]> ReadEntryAsync(ZipArchive archive, string path, int limit, CancellationToken token)
        {
            ZipArchiveEntry entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Missing APK entry '{path}'.");
            if (entry.Length > limit) { throw new InvalidDataException($"APK entry '{path}' exceeds the size limit."); }
            await using Stream stream = await entry.OpenAsync(token).ConfigureAwait(false);
            byte[] data = await BoundedStreamReader.ReadAsync(stream, (int)entry.Length, token).ConfigureAwait(false);
            if (data.Length != entry.Length) { throw new InvalidDataException($"Truncated APK entry '{path}'."); }
            return data;
        }
    }
}
