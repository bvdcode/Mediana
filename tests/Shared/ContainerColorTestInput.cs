// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class ContainerColorTestInput
    {
        private const string UntaggedHash = "E0D88B5D9D593C8CBEB552764095351968C75C9296939D496AB16CBBACD0CE11";
        private const string ConflictHash = "5F42C51A79B91870AFF4AB2E7AF6FE1C9A98D74BC71A101E8B353ED2EC8781D1";

        public static async Task VerifyAsync(string root)
        {
            VideoColorInfo limited = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709,
                ColorRange.Limited, ChromaLocation.Left);
            await VerifyFileAsync(root, "container-709-limited.mp4", limited, UntaggedHash, false);
            await VerifyFileAsync(root, "container-709-full.mp4", limited with { Range = ColorRange.Full }, UntaggedHash, false);
            await VerifyFileAsync(root, "container-709.mov", limited, UntaggedHash, false);
            await VerifyFileAsync(root, "container-601-conflict.mp4", limited with
            { Primaries = ColorPrimaries.Smpte170M, Matrix = MatrixCoefficients.Bt601, Range = ColorRange.Full }, ConflictHash, true);
            await VerifyFileAsync(root, "container-2020-pq.mp4", limited with
            { Primaries = ColorPrimaries.Bt2020, Transfer = TransferFunction.Pq, Matrix = MatrixCoefficients.Bt2020NonConstant }, UntaggedHash, false);
        }

        private static async Task VerifyFileAsync(string root, string name, VideoColorInfo expected, string expectedHash, bool taggedCodec)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).AddStreamMetadataReader(new H264StreamMetadataReader()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name)));
            await using VideoSession session = await engine.OpenVideoAsync(source, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            VideoColorDescription description = session.Stream.ColorDescription
                ?? throw new InvalidDataException("The container color description was lost before decoding.");
            using IncrementalHash samples = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[]? rgbReference = null;
            if (expected.Transfer != TransferFunction.Pq)
            {
                rgbReference = await File.ReadAllBytesAsync(Path.Combine(root, Path.GetFileNameWithoutExtension(name) + ".frame0.srgb.rgba"));
            }
            int count = 0;
            VideoFrame? next;
            while ((next = await session.ReadFrameAsync()) is not null)
            {
                using VideoFrame frame = next;
                if (!ReferenceEquals(description, frame.Format.ColorDescription) || frame.Format.GetColorInterpretation() != expected ||
                    frame.Format.Color.Range != ColorRange.Limited || frame.Format.Color.Primaries !=
                    (taggedCodec ? ColorPrimaries.Bt709 : ColorPrimaries.Unspecified))
                {
                    throw new InvalidDataException("The container color precedence or original codec color labels changed.");
                }
                for (int plane = 0; plane < frame.Planes.Count; plane++)
                {
                    for (int row = 0; row < frame.Planes[plane].Height; row++)
                    {
                        samples.AppendData(frame.GetRow(plane, row));
                    }
                }
                if (count == 0 && expected.Transfer != TransferFunction.Pq)
                {
                    VerifyProcessing(frame, expected, session.Stream.TimeBase,
                        rgbReference ?? throw new InvalidDataException("The independent RGB reference is missing."));
                }
                count++;
            }
            if (count == 0 || Convert.ToHexString(samples.GetHashAndReset()) != expectedHash)
            {
                throw new InvalidDataException("The complete native-range YUV sequence differs from the independent decoder.");
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Container color decoding retained pooled buffers.");
            }
        }

        private static void VerifyProcessing(VideoFrame frame, VideoColorInfo expected, TimeBase timeBase, byte[] rgbReference)
        {
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            using (VideoFrame retained = frame.Retain())
            using (VideoFrame converted = SrgbConverter.Apply(retained, allocator))
            using (VideoFrame explicitColor = SrgbConverter.Apply(retained, expected, allocator))
            {
                if (!converted.Data.Span.SequenceEqual(explicitColor.Data.Span) || converted.Format.ColorDescription is not null ||
                    converted.Format.Color != VideoColorInfo.Srgb)
                {
                    throw new InvalidDataException("Default color conversion did not use the declared source interpretation.");
                }
                if (converted.Data.Length != rgbReference.Length)
                {
                    throw new InvalidDataException("The independent RGB reference has incorrect dimensions.");
                }
                for (int index = 0; index < rgbReference.Length; index++)
                {
                    if (Math.Abs(converted.Data.Span[index] - rgbReference[index]) > 1)
                    {
                        throw new InvalidDataException("Source color conversion differs from the independent sRGB reference.");
                    }
                }
            }
            using (VideoFrame preview = SrgbPreview.Fit(frame, VideoDisplayMatrix.Identity, 64, 64, allocator))
            using (VideoFrame explicitPreview = SrgbPreview.Fit(frame, expected, VideoDisplayMatrix.Identity, 64, 64, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            using (MediaBuffer explicitPng = PngEncoder.Encode(explicitPreview, allocator))
            {
                if (!png.Memory.Span.SequenceEqual(explicitPng.Memory.Span) || preview.Format.ColorDescription is not null)
                {
                    throw new InvalidDataException("Default preview did not preserve the source color interpretation.");
                }
            }
            using (Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(frame.Format, 64, 64, allocator))
            using (VideoFrame resized = resizer.Apply(frame))
            {
                if (!ReferenceEquals(resized.Format.ColorDescription, frame.Format.ColorDescription) ||
                    resized.Format.GetColorInterpretation() != expected || resized.Format.Color != frame.Format.Color)
                {
                    throw new InvalidDataException("Sample-domain resizing lost the source color description.");
                }
                VerifyEncoding(resized, expected, timeBase, allocator);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Container color processing retained pooled buffers.");
            }
        }

        private static void VerifyEncoding(VideoFrame frame, VideoColorInfo expected, TimeBase timeBase, MediaBufferAllocator allocator)
        {
            using H264Encoder encoder = H264Encoder.Create(frame.Format, timeBase, allocator);
            VideoStreamMetadata metadata = new H264StreamMetadataReader().Read(encoder.Stream) as VideoStreamMetadata
                ?? throw new InvalidDataException("The encoder did not expose its color configuration.");
            if (metadata.Configurations.Count != 1 || metadata.Configurations[0].Color != expected || encoder.Stream.ColorDescription is not null)
            {
                throw new InvalidDataException("The encoder did not express the effective source colors in its own bitstream.");
            }
            if (encoder.TrySend(frame) != CodecInputStatus.Accepted)
            {
                throw new InvalidOperationException("The encoder did not accept a frame with source color metadata.");
            }
            using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("The encoder produced no packet.");
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            decoder.TrySend(packet);
            decoder.CompleteInput();
            using VideoFrame decoded = decoder.Receive().Value ?? throw new InvalidDataException("The encoded packet produced no frame.");
            if (decoded.Format.Color != expected || decoded.Format.ColorDescription is not null)
            {
                throw new InvalidDataException("The encoded packet did not retain the effective source colors.");
            }
        }
    }
}
