// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Mediana.Containers.Mp4;
using Mediana.Codecs.Mpeg2;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.NativeTests
{
    internal static class NativeServerPreviewTests
    {
        internal static async Task RunAsync()
        {
            await VerifyMpeg2PreviewAsync();
            H264DirectRegionTestInput.Verify();
            Console.WriteLine("PASS H.264 direct regions: spatial/temporal motion, both inference sizes, every pixel and both reference lists.");
            H264FramePublicationTestInput.VerifyPublication();
            H264FramePublicationTestInput.VerifyCancellation();
            H264FramePublicationTestInput.VerifyConstructionFailure();
            Console.WriteLine("PASS H.264 publication: shared immutable pixels, crop/stride, independent frame/reference ownership and failed allocation cleanup.");
            PresentationTimingTestInput.VerifyRetainedPacket();
            await PresentationTimingTestInput.VerifyLimitsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));
            await PresentationTimingTestInput.VerifySignedAndUnknownAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));
            foreach (string name in new[] { "frame-rate-cfr.mp4", "frame-rate-vfr.mp4", "frame-rate-edited.mp4", "preview-hd.mp4" })
            {
                foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
                {
                    await PresentationTimingTestInput.VerifyReferenceAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name, mode);
                    await PresentationTimingTestInput.VerifyJointDecodingAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name, mode);
                }
            }
            Console.WriteLine("PASS presentation timing: every independent interval, original packet durations, bounded lazy storage, signed offsets and unknown duplicates.");
            await VideoWindowTestInput.VerifySelectionAsync();
            await VideoWindowTestInput.VerifyFailureAsync();
            await VideoWindowTestInput.VerifyCancellationAsync();
            await VideoWindowTestInput.VerifyConcurrencyAsync();
            foreach (string name in new[] { "frame-rate-cfr.mp4", "frame-rate-vfr.mp4", "frame-rate-edited.mp4", "preview-hd.mp4" })
            {
                await RealVideoWindowTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name);
            }
            Console.WriteLine("PASS video windows: exact half-open movie times, independent complete pixels, no pixel copies, cancellation and retained ownership.");
            await ServerAudioPreviewTestInput.VerifyCoversAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ServerAudioPreviewTestInput.VerifyWaveformsAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ServerAudioPreviewTestInput.VerifyRecognizedFailureAsync(Path.Combine(AppContext.BaseDirectory, "Data"), false);
            await ServerAudioPreviewTestInput.VerifyRecognizedFailureAsync(Path.Combine(AppContext.BaseDirectory, "Data"), true);
            Console.WriteLine("PASS audio previews: one container open, cover before waveform, original pixels, complete levels and failure cleanup.");
            await ContainerDecodeTransferTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS container transfer: complete MP4/MKV video pixels, restarted seek, MP4/MKV/MP3 audio and original storage ownership.");
            await ServerContainerPreviewTestInput.VerifyRealCoversAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ServerContainerPreviewTestInput.VerifySelectionAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ServerContainerPreviewTestInput.VerifyRecognizedFailureAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            ServerContainerPreviewTestInput.VerifyTargets();
            Console.WriteLine("PASS container previews: original cover priority, complete PNG/Exif pixels, recognized failures and declared target policy.");
            foreach (string name in new[] { "frame-rate-cfr.mp4", "frame-rate-vfr.mp4", "frame-rate-edited.mp4", "preview-hd.mp4" })
            {
                await ServerVideoPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name);
            }
            Console.WriteLine("PASS server video previews: midpoint targets, VFR, movie edits, exact pixels, out-of-order requests and EOF restart.");
            await ServerVideoPreviewTestInput.VerifyPngAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));
            Console.WriteLine("PASS server HD preview: complete sample PNG, dimensions, sRGB labels and every independent pixel.");
        }

        private static async Task VerifyMpeg2PreviewAsync()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MpegPsReaderFactory())
                .AddVideoDecoder(new Mpeg2DecoderFactory()).Build();
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "ReadSupport", "timed-mpeg2.mpg");
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            await using VisualSession session = await engine.OpenVisualAsync(source, new MediaBufferAllocator(4 * 1024 * 1024),
                new() { Request = new() { Time = new(1, new(1, 1)) } });
            using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing MPEG-2 preview frame.");
            if (frame.Timestamp != new MediaTimestamp(138600, new(1, 90000)) || frame.Format.PixelFormat != VideoPixelFormat.Yuv420P8
                || frame.Format.Width != 64 || frame.Format.Height != 48)
            {
                throw new InvalidDataException("MPEG-2 preview timing or decoded format differs from the reference.");
            }
            Console.WriteLine("PASS MPEG-2 timed preview: decoded the reference frame from a program stream.");
        }
    }
}
