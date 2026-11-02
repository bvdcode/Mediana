// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class JpegCoverTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FlacReaderFactory()).AddContainer(new Mp4ReaderFactory())
                .AddImageDecoder(new JpegImageDecoder(sourceColorSpace: JpegColorSpace.YCbCr, sampleAspectRatio: SampleAspectRatio.Square)).Build();
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Jpeg", "embedded-cover.rgba"));
            foreach ((string folder, string name) in new[] { ("Flac", "stereo-covers.flac"), ("Metadata", "covers.m4a") })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, folder, name));
                using MemoryStream input = new(encoded, writable: false);
                await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                await using ContainerSession session = await engine.OpenContainerAsync(source);
                using MediaBuffer picture = await session.ReadPictureAsync(1);
                await session.DisposeAsync();
                MediaBufferAllocator allocator = new(2688);
                using (VideoFrame frame = await engine.DecodeImageAsync(picture.Memory, allocator))
                {
                    picture.Dispose();
                    encoded.AsSpan().Clear();
                    if (session.AllocatedBufferBytes != 0 || allocator.AllocatedBytes != expected.Length ||
                        frame.Format.SampleAspectRatio != SampleAspectRatio.Square || frame.Data.Length != expected.Length)
                    {
                        throw new InvalidDataException("JPEG cover lifetime, working budget or geometry differ.");
                    }
                    for (int i = 0; i < expected.Length; i++)
                    {
                        if (Math.Abs(frame.Data.Span[i] - expected[i]) > 2)
                        {
                            throw new InvalidDataException("Extracted JPEG cover differs from independent complete pixels.");
                        }
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("JPEG cover retains media storage.");
                }
            }
        }
    }
}
