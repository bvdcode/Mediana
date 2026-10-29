using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeTestRunner
    {
        public static async Task RunAsync()
        {
            H264CabacTestInput.VerifyMixedBins();
            Console.WriteLine("PASS H.264 CABAC arithmetic: mixed decision, bypass and termination bins match exact intervals.");
            (string Name, string Extension, int Packet)[] cases =
                [("avc-tail", ".mov", 0), ("avc-tail", ".mov", 1), ("avc-tail", ".mov", 2),
                    ("avc-tail", ".mov", 3), ("avc-tail", ".mov", 4), ("avc-tail", ".mov", 5), ("avc-cropped", ".mp4", 0)];
            MediaBufferAllocator allocator = new(1024 * 1024);
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
            string h264Root = Path.Combine(AppContext.BaseDirectory, "Data", "H264");
            await H264BidirectionalTestInput.VerifyHeadersAsync(root);
            await H264BidirectionalTestInput.VerifyReferenceListsAsync(root);
            H264BidirectionalTestInput.VerifyMotion();
            H264BidirectionalDeblockingTestInput.Verify();
            H264BiPredictionTestInput.Verify();
            H264BidirectionalPredictionTestInput.Verify();
            Console.WriteLine("PASS H.264 B prediction components: independent L0/L1 motion and ordinary, explicit, implicit 8-bit sample weighting match fixed vectors.");
            Console.WriteLine("PASS H.264 B partitions: single L0/L1 and Bi modes, fractional luma/chroma compensation, all weighting modes and exact partition placement match.");
            Console.WriteLine("PASS H.264 B headers: reference and non-reference slices, both active lists, picture order and CABAC alignment match the independent trace.");
            Console.WriteLine("PASS H.264 B reference lists: parsed I/P/B picture order, retained reference B pictures and both lists match the expected first GOP order.");
            await Mp4TimelineTestInput.VerifyAsync(root);
            await HlsPlanTestInput.VerifyAsync(root);
            await HlsSessionTestInput.VerifyAsync(root);
            Console.WriteLine("PASS MP4 HLS session: exact AVC/AAC windows, VOD playlist, segments requested as 2, 0, 1, 2, all compressed packets and continuous transport state match.");
            await MpegTsSegmentTestInput.VerifyAsync(root);
            Console.WriteLine("PASS MPEG-TS segments: all 36 AVC and 339 AAC packets, clocks and counters match; restored segments in order 2, 0, 1, 2 equal continuous output byte for byte.");
            await Mp4SampleAccessTestInput.VerifyAsync(root, "avc-segments.mp4", Mp4TimestampMode.LinearMovie);
            Console.WriteLine("PASS AVC video segment plan: inspected IDR boundaries at 0, 3 and 6 seconds, exact durations and reverse access preserve decoder order and reader cursors.");
            foreach (string file in new[] { "avc-aac.mp4", "avc-tail.mov" })
            {
                await Mp4SampleAccessTestInput.VerifyAsync(root, file, Mp4TimestampMode.Media);
                await Mp4SampleAccessTestInput.VerifyAsync(root, file, Mp4TimestampMode.LinearMovie);
            }
            Console.WriteLine("PASS MP4 random sample access: every reverse and sequential AVC/AAC packet matches independent offsets, bytes, timing and sync flags in both timelines.");
            Console.WriteLine("PASS MP4 linear movie timeline: every AVC/AAC packet matches independent bytes, translated PTS/DTS and merged decode order.");
            foreach (string file in new[] { "avc-aac.mp4", "avc-tail.mov", "avc-cabac-8x8-2.mp4" })
            {
                await MpegTsTestInput.VerifyAsync(root, file);
                await MpegTsTestInput.VerifyAsync(root, file, Mp4TimestampMode.LinearMovie);
            }
            Console.WriteLine("PASS MPEG-TS remux: media and linear movie timelines, AVC/AAC access units, PTS/DTS, PAT/PMT, PCR cadence, continuity and released buffers match.");
            await H264CabacTransformTestInput.VerifyContextsAsync(h264Root);
            await H264CabacTransformTestInput.VerifyResidualsAsync(h264Root, root);
            Console.WriteLine("PASS H.264 CABAC 8x8: all 7072 initial states and every residual position match normative references and exact intervals.");
            await H264EightByEightTestInput.VerifyPredictionVectorsAsync(h264Root);
            await H264EightByEightTestInput.VerifyExplicitPicturesAsync(h264Root);
            (int intra8, int inter8) = await H264EightByEightTestInput.VerifySequenceAsync(root, "avc-cavlc-8x8", 18, null);
            (int weightedIntra8, int weightedInter8) = await H264EightByEightTestInput.VerifySequenceAsync(root, "avc-cavlc-8x8-weighted", 12, null);
            Console.WriteLine($"PASS H.264 CAVLC 8x8: nine explicit intra modes and 30 complete I/P pictures match; {intra8 + weightedIntra8} intra and {inter8 + weightedInter8} inter 8x8 macroblocks.");
            for (int initialization = 0; initialization < 3; initialization++)
            {
                (int intra, int inter) = await H264EightByEightTestInput.VerifySequenceAsync(root, $"avc-cabac-8x8-{initialization}", initialization == 0 ? 18 : 12, initialization);
                Console.WriteLine($"PASS H.264 CABAC 8x8 selection {initialization}: all complete I/P YUV pictures match; {intra} intra and {inter} inter 8x8 macroblocks.");
            }
            await H264CabacTestInput.VerifyIntraContextStatesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
            await H264CabacTestInput.VerifyRealIntraPrefixAsync(root);
            Console.WriteLine("PASS H.264 CABAC intra initialization: all 227 contexts at 52 quantizers and the real IDR prefix match.");
            await H264CabacTestInput.VerifyPredictiveContextStatesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
            await H264CabacTestInput.VerifyRealPredictivePrefixAsync(root);
            Console.WriteLine("PASS H.264 CABAC predictive initialization: 252 contexts at 52 quantizers, three initialization selections and the real P prefix match.");
            for (int initialization = 0; initialization < 3; initialization++)
            {
                await H264CabacPredictionTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), root, initialization);
            }
            Console.WriteLine("PASS H.264 CABAC P syntax: skip, macroblock/subblock types, reference indices and signed motion differences match exact intervals.");
            await H264CabacTestInput.VerifyIntraPicturesAsync(root);
            Console.WriteLine("PASS H.264 CABAC intra pictures: seven independent filtered YUV references and storage release match.");
            for (int initialization = 0; initialization < 3; initialization++)
            {
                await H264CabacTestInput.VerifyPredictivePicturesAsync(root, $"avc-cabac-p{initialization}", 12, 94, 62, initialization);
            }
            await H264CabacTestInput.VerifyPredictivePicturesAsync(root, "avc-cabac-motion", 18, 190, 126, 2);
            Console.WriteLine("PASS H.264 CABAC predictive pictures: 54 independent filtered YUV pictures, all initialization selections and storage release match.");
            using (H264ReferenceBuffer references = new())
            {
                H264PictureOrderState order = new();
                foreach ((string name, string extension, int packet) in cases)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + extension), packet);
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"{name}.frame{packet}.yuv"));
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, ref order, allocator, expected);
                    Console.WriteLine($"PASS {name}, packet {packet}: all YUV samples match.");
                }
                byte[] weighted = await File.ReadAllBytesAsync(Path.Combine(root, "avc-weighted.sequence.yuv"));
                const int frameBytes = 94 * 62 * 3 / 2;
                if (weighted.Length != frameBytes * 12)
                {
                    throw new InvalidDataException("The weighted H.264 reference sequence has an unexpected length.");
                }
                for (int index = 0; index < 12; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-weighted.mp4"), index);
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, ref order, allocator, weighted.AsSpan(index * frameBytes, frameBytes));
                    Console.WriteLine($"PASS avc-weighted, packet {index}: all YUV samples match.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("H.264 reference picture storage was not released.");
            }
            await NativePublicVideoTests.RunAsync(root);
            await NativeSrgbTests.RunAsync(root);
            await RotatedPreviewTestInput.VerifyAsync(root);
            Console.WriteLine("PASS rotated MP4 preview: display matrix, square-pixel 49x100 PNG, CRCs and all reference pixels match.");
            await SdrPreviewTestInput.VerifyAsync(root, "avc-cabac-intra", 3, 5, 94, 62, 49);
            Console.WriteLine("PASS CABAC MP4 preview: 450 ms selects 600 ms, square-pixel 100x49 PNG and all independent sRGB samples match.");
            for (int initialization = 0; initialization < 3; initialization++)
            {
                await SdrPreviewTestInput.VerifyAsync(root, $"avc-cabac-p{initialization}", 5, 10, 94, 62, 49);
            }
            Console.WriteLine("PASS CABAC P previews: all initialization selections, 450 ms selects 500 ms, timing, square-pixel PNG and independent sRGB samples match.");
            await SdrPreviewTestInput.VerifyAsync(root, "avc-cavlc-8x8-weighted", 6, 12, 190, 126, 50);
            Console.WriteLine("PASS High CAVLC 8x8 preview: 450 ms selects 500 ms, square-pixel 100x50 PNG and independent sRGB samples match.");
            await SdrPreviewTestInput.VerifyAsync(root, "avc-cabac-8x8-2", 6, 12, 190, 126, 50);
            Console.WriteLine("PASS High CABAC 8x8 preview: 450 ms selects 500 ms, square-pixel 100x50 PNG and independent sRGB samples match.");
        }
    }
}
