// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class PngCoverTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp3ReaderFactory()).AddContainer(new FlacReaderFactory())
                .AddContainer(new Mp4ReaderFactory()).AddContainer(new MatroskaReaderFactory())
                .AddImageDecoder(new PngImageDecoder()).Build();
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Png", "cover.rgba"));
            (string Folder, string Name, bool Attachment)[] cases =
            [
                ("Mp3", "stereo-vbr-cover.mp3", false),
                ("Flac", "stereo-covers.flac", false),
                ("Metadata", "covers.m4a", false),
                ("Matroska", "avc-aac-attachment.mkv", true)
            ];
            foreach ((string folder, string name, bool attachment) in cases)
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, folder, name));
                using MemoryStream input = new(encoded, writable: false);
                await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 131072 });
                using MediaBuffer cover = attachment
                    ? await session.ReadAttachmentAsync(0)
                    : await session.ReadPictureAsync(0);
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes < cover.Memory.Length)
                {
                    throw new InvalidDataException("The extracted PNG lease must retain its accounted backing storage after container disposal.");
                }
                MediaBufferAllocator allocator = new(expected.Length + 2 * 24 * 3);
                using (VideoFrame frame = await engine.DecodeImageAsync(cover.Memory, allocator))
                {
                    cover.Dispose();
                    encoded.AsSpan().Clear();
                    if (session.AllocatedBufferBytes != 0 || allocator.AllocatedBytes != expected.Length ||
                        !frame.Data.Span.SequenceEqual(expected) || frame.Format.Width != 24 || frame.Format.Height != 16 ||
                        frame.Format.Color.Primaries != ColorPrimaries.Unspecified || frame.Format.Color.Transfer != TransferFunction.Unspecified)
                    {
                        throw new InvalidDataException("Extracted PNG pixels, metadata or independent frame ownership differ.");
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("PNG cover decoding retained media buffers.");
                }
            }
        }
    }
}
