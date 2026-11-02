// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264FramePublicationTestInput
    {
        public static void VerifyPublication()
        {
            foreach (H264SliceKind kind in new[] { H264SliceKind.I, H264SliceKind.P, H264SliceKind.B })
            {
                foreach (bool crop in new[] { false, true })
                {
                    VerifyPicture(kind, crop);
                }
            }
        }

        private static void VerifyPicture(H264SliceKind kind, bool crop)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet sequence = Sequence(crop);
            using H264Picture picture = new(sequence, allocator, kind);
            for (int plane = 0; plane < 3; plane++)
            {
                Span<byte> samples = picture.Plane(plane);
                for (int index = 0; index < samples.Length; index++)
                {
                    samples[index] = (byte)((index + plane * 50) % 256);
                }
            }
            long originalBytes = allocator.AllocatedBytes;
            using WritableMediaBuffer blocker = allocator.Allocate(checked((int)(allocator.LimitBytes - originalBytes)));
            MediaTimestamp timestamp = new(7, new(1, 30));
            MediaDuration duration = new(1, new(1, 30));
            using VideoFrame first = H264FramePublisher.Publish(picture, timestamp, duration, default);
            using VideoFrame second = H264FramePublisher.Publish(picture, timestamp, duration, default);
            Require(allocator.AllocatedBytes == allocator.LimitBytes && first.Data.Equals(second.Data));
            Require(first.Data.Length == 32 * 32 * 3 / 2 && first.Timestamp == timestamp && first.Duration == duration);
            Require(first.Format.SampleAspectRatio == new SampleAspectRatio(4, 3) && first.Format.Color.Primaries == ColorPrimaries.Bt709);
            for (int plane = 0; plane < 3; plane++)
            {
                bool immutable = false;
                try { picture.Plane(plane); }
                catch (InvalidOperationException) { immutable = true; }
                Require(immutable && picture.ReadPlane(plane)[0] == plane * 50);
            }
            VerifyPixels(first, sequence);
            using VideoFrame retained = first.Retain();
            first.Dispose();
            picture.Dispose();
            blocker.Dispose();
            Require(allocator.AllocatedBytes == 32 * 32 * 3 / 2);
            VerifyPixels(second, sequence);
            VerifyPixels(retained, sequence);
            second.Dispose();
            Require(allocator.AllocatedBytes == 32 * 32 * 3 / 2);
            retained.Dispose();
            Require(allocator.AllocatedBytes == 0);
            bool disposed = false;
            try { picture.PublishPixels(); }
            catch (ObjectDisposedException) { disposed = true; }
            Require(disposed);
        }

        public static void VerifyCancellation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(Sequence(true), allocator, H264SliceKind.P);
            picture.Plane(0).Fill(77);
            long original = allocator.AllocatedBytes;
            bool cancelled = false;
            try { H264FramePublisher.Publish(picture, null, null, new(true)); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && allocator.AllocatedBytes == original);
            picture.Plane(0)[0] = 99;
            Require(picture.ReadPlane(0)[0] == 99);
            picture.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        public static void VerifyConstructionFailure()
        {
            foreach (H264SliceKind kind in new[] { H264SliceKind.I, H264SliceKind.P, H264SliceKind.B })
            {
                MediaBufferAllocator probe = new(1024 * 1024);
                long required;
                using (H264Picture picture = new(Sequence(false), probe, kind))
                {
                    required = probe.AllocatedBytes;
                }
                foreach (long limit in new[] { 1, required - 32 * 32 * 3 / 2, required - 1 })
                {
                    MediaBufferAllocator allocator = new(limit);
                    bool limited = false;
                    try { using H264Picture rejected = new(Sequence(false), allocator, kind); }
                    catch (MediaMemoryLimitException) { limited = true; }
                    Require(limited && allocator.AllocatedBytes == 0);
                }
            }
        }

        private static void VerifyPixels(VideoFrame frame, H264SequenceParameterSet sequence)
        {
            Require(frame.Format.Width == sequence.Width && frame.Format.Height == sequence.Height);
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    ReadOnlySpan<byte> pixels = frame.GetRow(plane, row);
                    for (int column = 0; column < pixels.Length; column++)
                    {
                        int source = (row + sequence.CropTop / scale) * (32 / scale) + sequence.CropLeft / scale + column;
                        Require(pixels[column] == (byte)((source + plane * 50) % 256));
                    }
                }
            }
        }

        private static H264SequenceParameterSet Sequence(bool crop) => new()
        {
            WidthInMbs = 2,
            HeightInMapUnits = 2,
            FrameMbsOnly = true,
            CropLeft = crop ? 2 : 0,
            CropTop = crop ? 4 : 0,
            Width = crop ? 26 : 32,
            Height = crop ? 22 : 32,
            Vui = new() { AspectRatioWidth = 4, AspectRatioHeight = 3, ColorPrimaries = 1, TransferCharacteristics = 1, MatrixCoefficients = 1 }
        };

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Published H.264 pixels changed, shared writable memory or retained reconstruction storage.");
            }
        }
    }
}
