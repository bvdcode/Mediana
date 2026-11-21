// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264ProfileSequenceTestInput
    {
        public static async Task VerifyAsync(string path, VideoPixelFormat pixels, int width, int height, int count, int fps,
            IReadOnlyList<VideoScanMode>? scanModes = null, VideoFieldChromaLocations? fieldChroma = null)
        {
            await using FileStream compressed = File.OpenRead(path + ".yuv.gz");
            await using GZipStream decoded = new(compressed, CompressionMode.Decompress);
            using MemoryStream expected = new();
            await decoded.CopyToAsync(expected);
            byte[] reference = expected.ToArray();
            int frameBytes = checked(reference.Length / count);
            if (reference.Length % count != 0)
            {
                throw new InvalidDataException("The H.264 reference sequence has a partial frame.");
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path + ".mp4"));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame retained = await session.ReadFrameAsync()
                ?? throw new InvalidDataException("The H.264 profile sequence contains no frame.");
            Verify(retained, reference.AsSpan(0, frameBytes), pixels, width, height, 0, fps, scanModes?[0], fieldChroma);
            for (int index = 1; index < count; index++)
            {
                using VideoFrame frame = await session.ReadFrameAsync()
                    ?? throw new InvalidDataException("The H.264 profile sequence ended early.");
                Verify(frame, reference.AsSpan(index * frameBytes, frameBytes), pixels, width, height, index, fps, scanModes?[index], fieldChroma);
            }
            using (VideoFrame? extra = await session.ReadFrameAsync())
            {
                if (extra is not null)
                {
                    throw new InvalidDataException("The H.264 profile sequence returned an extra frame.");
                }
            }
            for (int index = count - 1; index >= 0; index--)
            {
                MediaTimestamp target = new(index, new TimeBase(1, fps));
                await session.SeekAsync(target);
                using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(target)
                    ?? throw new InvalidDataException("The H.264 profile seek returned no frame.");
                Verify(frame, reference.AsSpan(index * frameBytes, frameBytes), pixels, width, height, index, fps, scanModes?[index], fieldChroma);
            }
            await session.DisposeAsync();
            Verify(retained, reference.AsSpan(0, frameBytes), pixels, width, height, 0, fps, scanModes?[0], fieldChroma);
            retained.Dispose();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The H.264 profile session retained media buffers.");
            }
        }

        private static void Verify(VideoFrame frame, ReadOnlySpan<byte> reference, VideoPixelFormat pixels,
            int width, int height, int index, int fps, VideoScanMode? scan, VideoFieldChromaLocations? fieldChroma)
        {
            MediaTimestamp timestamp = frame.Timestamp ?? throw new InvalidDataException("The H.264 frame timestamp is missing.");
            MediaDuration duration = frame.Duration ?? throw new InvalidDataException("The H.264 frame duration is missing.");
            if (frame.Format.Width != width || frame.Format.Height != height || frame.Format.PixelFormat != pixels ||
                timestamp.CompareTo(new MediaTimestamp(index, new TimeBase(1, fps))) != 0 ||
                new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new MediaTimestamp(1, new TimeBase(1, fps))) != 0)
            {
                throw new InvalidDataException($"The H.264 frame differs from its reference: frame={index}; size={frame.Format.Width}x{frame.Format.Height}; format={frame.Format.PixelFormat.Name}; timestamp={timestamp.Value}*{timestamp.TimeBase.Numerator}/{timestamp.TimeBase.Denominator}; duration={duration.Value}*{duration.TimeBase.Numerator}/{duration.TimeBase.Denominator}.");
            }
            if (fieldChroma is not null && frame.Format.Color.FieldChromaLocations != fieldChroma)
            {
                throw new InvalidDataException("The H.264 output lost its independent field chroma locations.");
            }
            if (scan is VideoScanMode expectedScan && frame.Format.ScanMode != expectedScan)
            {
                throw new InvalidDataException($"The H.264 scan mode differs from the reference at frame {index}.");
            }
            VideoFramePresentation? expectedPresentation = scan switch
            {
                VideoScanMode.TopFieldFirst => VideoFramePresentation.Fields(VideoFieldParity.Top, 2),
                VideoScanMode.BottomFieldFirst => VideoFramePresentation.Fields(VideoFieldParity.Bottom, 2),
                VideoScanMode.Progressive or VideoScanMode.TopField or VideoScanMode.BottomField or null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(scan))
            };
            if (expectedPresentation is not null && frame.Presentation != expectedPresentation)
            {
                throw new InvalidDataException($"The H.264 field-pair presentation differs from the reference at frame {index}.");
            }
            if (pixels is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Gbr } &&
                (frame.Format.Color.Matrix != MatrixCoefficients.Rgb || frame.Format.Color.ChromaLocation != ChromaLocation.Unspecified ||
                frame.Format.Color.FieldChromaLocations is not null))
            {
                throw new InvalidDataException("Planar RGB output must not declare subsampled chroma positions.");
            }
            H264TestInput.VerifyFrame(frame, reference);
        }
    }
}
