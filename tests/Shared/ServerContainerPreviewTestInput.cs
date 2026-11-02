// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ServerContainerPreviewTestInput
    {
        internal static async Task VerifyRealCoversAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp3ReaderFactory())
                .AddContainer(new FlacReaderFactory()).AddContainer(new Mp4ReaderFactory())
                .AddContainer(new MatroskaReaderFactory()).AddImageDecoder(new PngImageDecoder()).Build();
            byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, "Png", "cover.rgba"));
            foreach (string name in new[] { "Mp3/stereo-vbr-cover.mp3", "Flac/stereo-covers.flac", "Metadata/covers.m4a", "Matroska/avc-aac-attachment.mkv" })
            {
                using MemoryStream input = new(await File.ReadAllBytesAsync(Path.Combine(root, name)), writable: false);
                await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                input.Position = 3;
                await using ContainerSession container = await engine.OpenContainerAsync(source);
                MediaBufferAllocator allocator = new(131072);
                using (VideoFrame preview = await Sample.VideoPreviewSample.ReadPreviewAsync(engine, container, allocator))
                {
                    await container.DisposeAsync();
                    Require(preview.Format.Width == 24 && preview.Format.Height == 16 && preview.Format.Color == VideoColorInfo.Srgb);
                    Require(preview.Data.Span.SequenceEqual(pixels) && allocator.AllocatedBytes == pixels.Length);
                    Require(container.AllocatedBufferBytes == 0 && input.Position == 3 && input.CanRead);
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static async Task VerifySelectionAsync(string root)
        {
            byte[] png = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.png"));
            foreach (int orientation in Enumerable.Range(1, 8))
            {
                string name = $"sequential-II-{orientation}";
                byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(root, "JpegExif", name + ".jpg"));
                byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, "JpegExif", name + ".rgba"));
                ContainerImageTestFactory factory = new([new(png, png.Length)],
                    [new("font data"u8.ToArray(), 9, MediaType: "image/jpeg", FileName: "cover.jpg"),
                     new(jpeg, jpeg.Length, MediaType: "font/ttf", FileName: "font.ttf")]);
                MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                    .AddImageDecoder(new PngImageDecoder()).AddImageDecoder(new JpegImageDecoder()).Build();
                await using EbmlElementTestSource source = new("IMGS"u8.ToArray());
                await using ContainerSession container = await engine.OpenContainerAsync(source);
                MediaBufferAllocator allocator = new(131072);
                using (VideoFrame preview = await Sample.VideoPreviewSample.ReadPreviewAsync(engine, container, allocator))
                {
                    await container.DisposeAsync();
                    Array.Clear(jpeg);
                    Require(preview.Data.Length == pixels.Length && preview.Format.Color == VideoColorInfo.Srgb);
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Require(Math.Abs(preview.Data.Span[i] - pixels[i]) <= ((i & 3) == 3 ? 0 : 4));
                    }
                    Require(factory.Reader!.PrefixReads.SequenceEqual(new[]
                    {
                        (ContainerImageSource.Attachment, 0), (ContainerImageSource.Attachment, 1)
                    }));
                    Require(factory.Reader.PayloadReads.SequenceEqual(new[] { (ContainerImageSource.Attachment, 1) }));
                    Require(factory.Reader.PacketReads == 0 && container.AllocatedBufferBytes == 0 && allocator.AllocatedBytes == pixels.Length);
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static async Task VerifyRecognizedFailureAsync(string root)
        {
            byte[] png = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.png"));
            byte[] corrupt = png[..16];
            ContainerImageTestFactory factory = new([new(png, png.Length)], [new(corrupt, corrupt.Length), new(png, png.Length)]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
            await using EbmlElementTestSource source = new("IMGS"u8.ToArray());
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            MediaBufferAllocator allocator = new(131072);
            bool rejected = false;
            try
            {
                using VideoFrame unexpected = await Sample.VideoPreviewSample.ReadPreviewAsync(engine, container, allocator);
            }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected && allocator.AllocatedBytes == 0 && factory.Reader!.PacketReads == 0);
            Require(factory.Reader!.PayloadReads.SequenceEqual(new[] { (ContainerImageSource.Attachment, 0) }));
            await container.DisposeAsync();
            Require(container.AllocatedBufferBytes == 0);
        }

        internal static void VerifyTargets()
        {
            Require(Sample.VideoPreviewSample.SelectTarget(null).Value == 0);
            Require(Sample.VideoPreviewSample.SelectTarget(new(0, new(1, 1))).Value == 0);
            Require(Sample.VideoPreviewSample.SelectTarget(new(1, new(1, 1))).Value == 0);
            MediaTimestamp target = Sample.VideoPreviewSample.SelectTarget(new(3, new(1, 2)));
            Require(target == new MediaTimestamp(7_500_000, TimeBase.TimeSpanTicks));
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Container preview changed source selection, independent pixels, geometry or storage ownership."); }
        }
    }
}
