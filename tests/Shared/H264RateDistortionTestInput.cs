// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264RateDistortionTestInput
    {
        public static Task VerifyAsync(string root, int mode, int qp, H264RateControlOptions? rate = null) =>
            H264CabacPictureTestInput.VerifyAsync(root, mode, qp, rate, H264MacroblockAnalysis.RateDistortion);

        public static void VerifyLifecycle()
        {
            MediaBufferAllocator allocator = new(1_000_000);
            VideoFormat format = new(46, 34, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left),
                new(4, 3));
            using (VideoFrame first = H264TemporalEncodingTestInput.CreateFrame(format, allocator, 3, 0,
                new(0, new(1, 1000)), new(40, new(1, 1000))))
            using (VideoFrame second = H264TemporalEncodingTestInput.CreateFrame(format, allocator, 3, 1,
                new(40, new(1, 1000)), new(40, new(1, 1000))))
            {
                long borrowed = allocator.AllocatedBytes;
                using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator,
                    new() { EntropyCoding = H264EntropyCoding.Cabac, MacroblockAnalysis = H264MacroblockAnalysis.RateDistortion });
                using MediaPacket initial = Send(first);
                using MediaPacket inter = Send(second);
                if (initial.IsKeyFrame is not true || inter.IsKeyFrame is not false) { throw new InvalidDataException("Predictive analysis changed GOP boundaries."); }
                encoder.Reset();
                using MediaPacket replayFirst = Send(first);
                using MediaPacket replaySecond = Send(second);
                if (!initial.Data.Span.SequenceEqual(replayFirst.Data.Span) || !inter.Data.Span.SequenceEqual(replaySecond.Data.Span))
                { throw new InvalidDataException("Reset did not discard the rate-distortion reference and syntax states."); }
                initial.Dispose(); inter.Dispose(); replayFirst.Dispose(); replaySecond.Dispose();
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                try
                {
                    encoder.TrySend(second, cancellation.Token);
                    throw new InvalidDataException("Cancellation was not observed during predictive analysis.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                if (allocator.AllocatedBytes != borrowed) { throw new InvalidDataException("Cancellation retained reference or decision buffers."); }
                encoder.Reset();
                using MediaPacket afterCancellation = Send(first);
                if (afterCancellation.IsKeyFrame is not true) { throw new InvalidDataException("Recovery after cancellation did not restart independently."); }
                byte[] owned = afterCancellation.Data.ToArray();
                encoder.Dispose();
                if (!owned.AsSpan().SequenceEqual(afterCancellation.Data.Span))
                { throw new InvalidDataException("Disposal invalidated a caller-owned encoded packet."); }

                MediaPacket Send(VideoFrame input)
                {
                    if (encoder.TrySend(input) != CodecInputStatus.Accepted) { throw new InvalidDataException("Drained encoder did not accept a picture."); }
                    return encoder.Receive().Value ?? throw new InvalidDataException("Missing rate-distortion picture.");
                }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Rate-distortion lifecycle retained media storage."); }
        }
    }
}
