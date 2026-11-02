// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Mediana.Core.Video;

namespace Mediana.NativeTests
{
    internal static class NativeVideoEncodingTests
    {
        public static async Task RunAsync(string root, string h264Root)
        {
            H264BitWritingTestInput.Verify();
            Console.WriteLine("PASS H.264 bit writing: all alignments and widths, independent NAL bytes, escaping, overflow and checkpoint restoration match.");
            H264SourceBlockTestInput.VerifyAllocation();
            foreach ((int width, int height) in new (int, int)[] { (1, 1), (2, 3), (17, 19), (32, 16), (35, 33) })
            {
                H264SourceBlockTestInput.Verify(width, height, false);
                H264SourceBlockTestInput.Verify(width, height, true);
            }
            Console.WriteLine("PASS H.264 source blocks: every plane, visible edges and padded rows match independent transform coefficients without changing input storage.");
            foreach ((int mode, int qp) in new (int, int)[] { (0, 0), (1, 0), (0, 6), (1, 6) })
            {
                await H264PcmEncodingTestInput.VerifyAsync(h264Root, mode, qp);
            }
            Console.WriteLine("PASS H.264 PCM encoding: I/P PCM, transformed neighbours, skip runs, cropped strided input, fixed QP, macroblock bit limits and complete independent pixels match.");
            await H264LevelBurstTestInput.VerifyAsync(h264Root);
            Console.WriteLine("PASS H.264 level burst: a fixed-QP initial picture exceeds the instantaneous rate and matches independent complete pixels, timing, format and reset state.");
            await HlsEncodingTestInput.VerifyAsync(root, "avc-multislice-cabac", 96, 80, 18, 6);
            await HlsEncodingTestInput.VerifyAsync(root, "avc-multislice-cabac", 48, 40, 18, 6);
            await HlsEncodingTestInput.VerifyAsync(root, "avc-weighted", 48, 30, 12, 3);
            await AvcAudioHlsTestInput.VerifyAsync(root, "avc-aac.mp4", "mp4a.40.2");
            await AvcAudioHlsTestInput.VerifyAsync(root, "avc-mp3-44.mp4", "mp4a.6B");
            await AvcAudioHlsTestInput.VerifyAsync(root, "avc-mp3-22.mp4", "mp4a.69");
            Console.WriteLine("PASS audiovisual HLS encoding: every AAC and MPEG-1/2 Layer III packet, timestamp and transport counter is preserved alongside encoded video.");
            Console.WriteLine("PASS HLS encoding: 48 independent reference pictures, 15 aligned IDR segments, exact transport timing and released output ownership match.");
            await YuvScalingTestInput.VerifySyntheticAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Resize"));
            foreach (ChromaLocation location in new[] { ChromaLocation.Left, ChromaLocation.Center, ChromaLocation.TopLeft,
                ChromaLocation.Top, ChromaLocation.BottomLeft, ChromaLocation.Bottom })
            {
                YuvResizeOwnershipTestInput.Verify(location, false);
                YuvResizeOwnershipTestInput.Verify(location, true);
            }
            Console.WriteLine("PASS YUV resize ownership: unchanged tight and strided frames retain their original budget with independent leases across six chroma locations and known or unknown SAR.");
            await YuvScalingTestInput.VerifySequenceAsync(root, "avc-multislice-cabac", 48, 40, 18, new(1, 1));
            await YuvScalingTestInput.VerifySequenceAsync(root, "avc-weighted", 48, 30, 12, new(235, 186));
            Console.WriteLine("PASS YUV area scaling: six chroma locations and 30 complete video pictures match exact rational references, preserving timing and display aspect.");
            await H264EncodingTestInput.VerifyAsync(root, "avc-multislice-cabac", 26, 18);
            await H264EncodingTestInput.VerifyAsync(root, "avc-multislice-cavlc", 38, 18);
            await H264EncodingTestInput.VerifyAsync(root, "avc-weighted", 18, 12);
            Console.WriteLine("PASS H.264 encoding: 48 fixed-QP IDR pictures match every independently decoded YUV sample and preserve timing, color and SAR.");
            foreach ((int mode, int qp) in new (int, int)[] { (0, 26), (1, 26), (2, 38), (3, 18), (1, 0), (1, 51) })
            {
                await H264TemporalEncodingTestInput.VerifyAsync(h264Root, mode, qp);
            }
            Console.WriteLine("PASS H.264 temporal encoding: 480 I/P pictures, complete independent pixels, VFR timing, IDR intervals, frame-number rollover and allocation release match.");
            foreach ((int mode, int qp, int rate, int buffer) in new (int, int, int, int)[]
                { (0, 26, 8000, 250), (1, 26, 32000, 200), (2, 26, 48000, 200), (3, 26, 40000, 300), (1, 0, 32000, 200), (3, 51, 40000, 300) })
            {
                await H264RateEncodingTestInput.VerifyAsync(h264Root, mode, qp, rate, buffer);
            }
            Console.WriteLine("PASS H.264 rate control: 480 complete pictures, independent samples, sliding rate envelopes, adaptive slice quantizers and released allocation match.");
        }
    }
}
