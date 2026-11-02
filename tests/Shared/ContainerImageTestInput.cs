// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Containers;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ContainerImageTestInput
    {
        private static byte[] Recognized => [137, 80, 78, 71, 13, 10, 26, 10, 9, 8, 7];
        private static ContainerImageSearchOptions AttachmentsFirst => new([ContainerImageSource.Attachment, ContainerImageSource.Picture]);

        internal static async Task VerifyRealFilesAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp3ReaderFactory()).AddContainer(new FlacReaderFactory())
                .AddContainer(new Mp4ReaderFactory()).AddContainer(new MatroskaReaderFactory())
                .AddImageDecoder(new PngImageDecoder()).AddImageDecoder(new JpegImageDecoder()).Build();
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.png"));
            byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, "Png", "cover.rgba"));
            (string Folder, string File, ContainerImageSource Source)[] cases =
            [
                ("Mp3", "stereo-vbr-cover.mp3", ContainerImageSource.Picture),
                ("Flac", "stereo-covers.flac", ContainerImageSource.Picture),
                ("Metadata", "covers.m4a", ContainerImageSource.Picture),
                ("Matroska", "avc-aac-attachment.mkv", ContainerImageSource.Attachment)
            ];
            foreach ((string folder, string file, ContainerImageSource kind) in cases)
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, folder, file));
                using MemoryStream stream = new(encoded, writable: false);
                await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                stream.Position = 3;
                await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 131072 });
                using ContainerImage image = await engine.ReadContainerImageAsync(session, AttachmentsFirst)
                    ?? throw new InvalidDataException("The real container image is missing.");
                Require(image.Source == kind && image.Index == 0 && image.DecoderImplementationId == "png.managed.image" && image.Data.Span.SequenceEqual(expected));
                using Core.MediaPacket packet = await session.ReadPacketAsync() ?? throw new InvalidDataException("The first packet is missing after image selection.");
                Require(stream.Position == 3);
                packet.Dispose();
                await session.DisposeAsync();
                Require(image.Data.Span.SequenceEqual(expected));
                MediaBufferAllocator allocator = new(pixels.Length + 2 * 24 * 3);
                using (VideoFrame frame = await engine.DecodeImageAsync(image.Data, allocator))
                {
                    image.Dispose();
                    encoded.AsSpan().Clear();
                    Require(session.AllocatedBufferBytes == 0 && frame.Data.Span.SequenceEqual(pixels) && allocator.AllocatedBytes == pixels.Length);
                }
                Require(allocator.AllocatedBytes == 0);
            }
            MediaEngine jpegOnly = new MediaEngineBuilder().AddContainer(new FlacReaderFactory()).AddContainer(new Mp4ReaderFactory())
                .AddImageDecoder(new JpegImageDecoder()).Build();
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.jpg"));
            foreach ((string folder, string file) in new[] { ("Flac", "stereo-covers.flac"), ("Metadata", "covers.m4a") })
            {
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, folder, file)));
                await using ContainerSession session = await jpegOnly.OpenContainerAsync(source);
                using ContainerImage image = await jpegOnly.ReadContainerImageAsync(session, new([ContainerImageSource.Picture]))
                    ?? throw new InvalidDataException("The JPEG registry did not select the original JPEG cover.");
                Require(image.Index == 1 && image.Data.Span.SequenceEqual(jpeg));
            }
        }

        internal static async Task VerifyPriorityAsync()
        {
            ContainerImageTestEntry[] pictures =
            [
                new("https://example.invalid/cover.png"u8.ToArray(), 33, PictureDataKind.ExternalReference),
                new([], 0), new(Recognized, 11, MediaType: "audio/mpeg")
            ];
            ContainerImageTestEntry[] attachments =
            [
                new(new byte[8], 3L * 1024 * 1024 * 1024, MediaType: "image/png", FileName: "cover.png"),
                new(Recognized, 11, MediaType: "font/ttf", FileName: "not-an-image.ttf")
            ];
            foreach (ContainerImageSource first in new[] { ContainerImageSource.Picture, ContainerImageSource.Attachment })
            {
                ContainerImageTestFactory factory = new(pictures, attachments);
                MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
                using MemoryStream stream = new("IMGS"u8.ToArray());
                await using RandomAccessStreamByteSource source = new(stream);
                await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 11 });
                ContainerImageSearchOptions options = first switch
                {
                    ContainerImageSource.Picture => new([ContainerImageSource.Picture, ContainerImageSource.Attachment]),
                    ContainerImageSource.Attachment => AttachmentsFirst,
                    _ => throw new ArgumentOutOfRangeException(nameof(first))
                };
                using ContainerImage image = await engine.ReadContainerImageAsync(session, options)
                    ?? throw new InvalidDataException("Container image priority did not select a recognized payload.");
                int expectedIndex = first switch
                {
                    ContainerImageSource.Picture => 2,
                    ContainerImageSource.Attachment => 1,
                    _ => throw new ArgumentOutOfRangeException(nameof(first))
                };
                Require(image.Source == first && image.Index == expectedIndex && image.Data.Span.SequenceEqual(Recognized));
                Require(factory.Reader!.PayloadReads.Count == 1 && factory.Reader.PayloadReads[0] == (image.Source, image.Index));
                Require(factory.Reader.PacketReads == 0 && session.AllocatedBufferBytes == 11);
                await session.DisposeAsync();
                Require(image.Data.Span.SequenceEqual(Recognized));
                image.Dispose();
                Require(session.AllocatedBufferBytes == 0);
            }
        }

        internal static async Task VerifyLimitsAsync()
        {
            ContainerImageTestFactory factory = new([], [new(new byte[8], 3L * 1024 * 1024 * 1024), new(Recognized, 11)]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
            using MemoryStream stream = new("IMGS"u8.ToArray());
            await using RandomAccessStreamByteSource source = new(stream);
            await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 11 });
            await RejectAsync<NotSupportedException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst with { MaxCandidates = 1 }));
            Require(factory.Reader!.PayloadReads.Count == 0 && session.AllocatedBufferBytes == 0);
            await RejectAsync<NotSupportedException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst with { Image = new() { MaxInputBytes = 1 } }));
            Require(factory.Reader.PayloadReads.Count == 0 && session.AllocatedBufferBytes == 0);
            await RejectAsync<NotSupportedException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst with { Image = new() { MaxProbeBytes = 7 } }));
            await RejectAsync<ArgumentOutOfRangeException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst with { MaxCandidates = 0 }));
            await RejectAsync<ArgumentNullException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst with { Image = null! }));
            Require(factory.Reader.PacketReads == 0 && session.AllocatedBufferBytes == 0);
            using ContainerImage image = await engine.ReadContainerImageAsync(session, AttachmentsFirst) ?? throw new InvalidDataException("The session became unusable after an image selection limit.");
            image.Dispose();
            await session.DisposeAsync();
            await RejectAsync<ObjectDisposedException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst));
        }

        internal static async Task VerifyEmptyAsync()
        {
            ContainerImageTestFactory factory = new([], []);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
            using MemoryStream stream = new("IMGS"u8.ToArray());
            await using RandomAccessStreamByteSource source = new(stream);
            await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 4 });
            Require(await engine.ReadContainerImageAsync(session, AttachmentsFirst) is null && session.AllocatedBufferBytes == 0);
            Require(factory.Reader!.PrefixReads.Count == 0 && factory.Reader.PayloadReads.Count == 0 && factory.Reader.PacketReads == 0);
            await session.DisposeAsync();
            await RejectAsync<ObjectDisposedException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst));
        }

        internal static async Task VerifyRecognizedFailureAsync(string root)
        {
            byte[] valid = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.png"));
            ContainerImageTestFactory factory = new([new(Recognized, 11), new(valid, valid.Length)], []);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
            using MemoryStream stream = new("IMGS"u8.ToArray());
            await using RandomAccessStreamByteSource source = new(stream);
            await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 11 });
            using ContainerImage image = await engine.ReadContainerImageAsync(session, AttachmentsFirst)
                ?? throw new InvalidDataException("A recognized image must be returned before complete decoding.");
            Require(image.Index == 0 && factory.Reader!.PayloadReads.Count == 1);
            MediaBufferAllocator allocator = new(1024);
            bool rejected = false;
            try
            {
                using VideoFrame unexpected = await engine.DecodeImageAsync(image.Data, allocator);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Require(rejected && allocator.AllocatedBytes == 0 && factory.Reader!.PayloadReads.Count == 1);
            image.Dispose();
            Require(session.AllocatedBufferBytes == 0);
        }

        internal static async Task VerifyCancellationAsync(bool afterPayload)
        {
            ContainerImageTestFactory factory = new([new(Recognized, 11)], []);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
            using MemoryStream stream = new("IMGS"u8.ToArray());
            await using RandomAccessStreamByteSource source = new(stream);
            await using ContainerSession session = await engine.OpenContainerAsync(source, new() { BufferBudgetBytes = 11 });
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (afterPayload)
            {
                factory.Reader!.AfterPayload = () => new ValueTask(cancellation.CancelAsync());
            }
            else
            {
                factory.Reader!.BeforePrefix = async token =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                };
            }
            Task<ContainerImage?> pending = engine.ReadContainerImageAsync(session, AttachmentsFirst, cancellation.Token).AsTask();
            if (!afterPayload)
            {
                await entered.Task.ConfigureAwait(false);
                await RejectAsync<InvalidOperationException>(async () => { await session.ReadPacketAsync(); return null; });
                await RejectAsync<InvalidOperationException>(() => engine.ReadContainerImageAsync(session, AttachmentsFirst));
                await cancellation.CancelAsync();
            }
            await RejectAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            Require(session.AllocatedBufferBytes == 0);
        }

        private static async Task RejectAsync<T>(Func<ValueTask<ContainerImage?>> operation) where T : Exception
        {
            try
            {
                using ContainerImage? unexpected = await operation().ConfigureAwait(false);
            }
            catch (T)
            {
                return;
            }
            throw new InvalidDataException("The container image search did not reject the requested operation.");
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Container image recognition, priority, ownership or allocation differs.");
            }
        }
    }
}
