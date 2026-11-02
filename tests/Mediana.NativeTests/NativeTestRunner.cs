// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeTestRunner
    {
        public static async Task RunAsync()
        {
            await NativeEbmlTests.RunAsync();
            foreach (bool stereo in new[] { false, true })
            {
                foreach (AudioStorageLayout layout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
                {
                    AudioDownmixTestInput.Verify(stereo, layout);
                    AudioDownmixTestInput.Verify(stereo, layout, AudioSampleFormat.Float32);
                    AudioDownmixTestInput.Verify(stereo, layout, AudioSampleFormat.Float32, sideSurround: true);
                }
            }
            AudioDownmixTestInput.VerifyBalance();
            Console.WriteLine("PASS explicit audio downmix: shuffled speakers, mono/stereo coefficients, correlated peaks, timing and released storage.");
            await NativeH264KernelTests.RunAsync();
            foreach (int configuration in new[] { 3, 4, 5, 6, 7 })
            {
                await AacMultichannelTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacMultichannel"), configuration);
            }
            Console.WriteLine("PASS multichannel AAC-LC: configurations 3–7, every speaker sample, timing, EOF and retained frame ownership match independent decoding.");
            await AacMultichannelTestInput.VerifyTransitionsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacMultichannel"));
            Console.WriteLine("PASS stereo AAC-LC: every window sequence/shape transition and independent/common window histories match FAAD2 PCM.");
            LanguageTagTestInput.Verify();
            Console.WriteLine("PASS language tag syntax: RFC 5646 examples and malformed subtags match without a registry lookup.");
            await Mp4TrackMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));
            Console.WriteLine("PASS track metadata: native scopes, localized user-data names, owned payloads and complete packet references.");
            await Mp4HeaderDateTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));
            Console.WriteLine("PASS header dates: unsigned v0/v1 fields, UTC/unspecified zones, distinct scopes and complete packet references.");
            await Mp4FrameRateTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));
            Console.WriteLine($"PASS AAC spectral codebook selection: {AacCodebookSelectionTestInput.VerifyTuples()} complete tuple/sign cases and {AacCodebookSelectionTestInput.VerifyBands()} long/short/escaped bands preserve exact costs, bytes and decoded values.");
            await NativeServerPreviewTests.RunAsync();
            Console.WriteLine("PASS indexed video rates: exact fractional CFR, VFR, reordered/edited samples, both timelines and complete packet references.");
            await QuickTimeAacTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));
            Console.WriteLine("PASS QuickTime AAC: both complete sound-v1 tracks, original timing, independent PNS energy and retained ownership.");
            foreach (bool noiseLeft in new[] { false, true })
            {
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    AacStereoNoiseTestInput.Verify(noiseLeft, mode);
                }
            }
            Console.WriteLine("PASS one-channel AAC noise: all M/S mask modes retain exact band energy and the independent spectral channel.");
            await ContainerColorTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Preview"));
            await AvcColorRemuxTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Preview"));
            Console.WriteLine("PASS container colors: nclx/nclc precedence, native-range YUV sequences, SDR previews, resizing and encoding.");
            await ContainerPictureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS container pictures: MP3, FLAC and MP4 original bytes, empty lists, packet cursors and owned backing storage.");
            await EncodingOperationTestInput.VerifyAsync();
            await EncodingGroupTestInput.VerifyAsync();
            Console.WriteLine("PASS encode operation: multiple packets, delayed output, finalization and frame ownership.");
            await MultiStreamTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4"));
            await NativePdfTests.RunAsync();
            await NativePngTests.RunAsync();
            await NativeJpegTests.RunAsync();
            await NativeContainerImageTests.RunAsync();
            BoundedStreamReadTestInput.VerifySynchronous();
            BoundedStreamReadTestInput.VerifyAllocationSynchronous();
            await BoundedStreamReadTestInput.VerifyAsync();
            await BoundedStreamReadTestInput.VerifyAllocationAsync();
            await VectorImageEngineTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ImageOutputAllocationTestInput.VerifyAsync();
            await ImageOutputAllocationTestInput.VerifyHeicInputAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif", "basic-8bit.heic"));
            Console.WriteLine("PASS image output allocation: SVG, TGS and PDF complete RGBA frames stay below the duplicate-buffer threshold and release every lease.");
            Console.WriteLine("PASS registered vector decoding: 352 SVG cases, 33 timed TGS frames and PDF pages match independent pixels.");
            await ImageProbeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS image metadata: SVG, TGS, PDF, HEIF, JPEG 2000 and JBIG2 dimensions and source ownership.");
            await ImageEngineTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS registered image decoding: HEIC, JPEG 2000 and JBIG2 match independent pixels with owned frames and unchanged source cursors.");
            await ImageFrameTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS image conversion: HEIC, JPEG 2000 and JBIG2 produce owned frames and complete sRGB PNG files.");
            await TgsReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Tgs"));
            Console.WriteLine("PASS TGS/Lottie: 33 supported frames match every reference channel within one RGB code and exact alpha.");
            SceneBufferOwnershipTestInput.VerifyReuse();
            SceneBufferOwnershipTestInput.VerifyTrim();
            SceneBufferOwnershipTestInput.VerifyDisposal();
            SceneBufferOwnershipTestInput.VerifyFailedConstruction();
            SceneBufferOwnershipTestInput.VerifyReservationDisposal();
            Console.WriteLine("PASS drawing surfaces: reuse, trimming, disposal, construction failure and exact effect budgets.");
            await SvgReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Svg"));
            Console.WriteLine("PASS SVG: 352 filter pixel references and Latin/Cyrillic font rendering.");
            await HeifReferenceTestInput.VerifyWavefrontAsync();
            await HeifReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif"));
            await HeifFrameOwnershipTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif"));
            Console.WriteLine("PASS HEIC: 42 independent full RGBA references, transforms, grids and alpha.");
            await Jbig2ReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jbig2"));
            Console.WriteLine("PASS JBIG2: 98 authored pixel references match all packed page bytes.");
            await Jpeg2000ReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg2000"));
            Console.WriteLine("PASS JPEG 2000: 70 independent component references, including native precision and signed samples.");
            await Mp3DecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
            await Console.Out.WriteLineAsync("PASS MP3 PCM: complete real files and all-rate stereo/window vectors, every sample, timing and retained ownership match.");
            Mp3HuffmanTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", "huffman-codes.tsv"));
            Console.WriteLine("PASS MP3 Huffman: all 1410 normative codewords, escape variants, signs, bit alignments and bounded quadruple endings match.");
            await Mp3SyntaxTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
            await Console.Out.WriteLineAsync("PASS MP3 spectrum: all 846 frames, protected-field CRCs, window parameters, scalefactors, complete bit-reservoir bytes and quantized coefficients match.");
            await Mp3ReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
            await Console.Out.WriteLineAsync("PASS MP3 framing: all independent MPEG1/2/2.5 packets, timestamps, ID3 text, picture bytes, trailing tags and ownership match.");
            foreach (string name in FlacDecodeTestInput.Names)
            {
                await FlacDecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), name, 127);
                Console.WriteLine($"PASS FLAC {name}: every PCM byte, sample count, timestamp and retained output match the independent lossless reference.");
            }
            foreach (string name in new[] { "stereo-covers", "mono-speech" })
            {
                await FlacWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), name);
            }
            Console.WriteLine("PASS FLAC waveform: all 400 Hz PCM16 RMS/peak windows, counts and timestamps match the original WAVE sources exactly.");
            await FlacMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), 127);
            Console.WriteLine("PASS native FLAC reader: independent STREAMINFO/tags, exact PNG/JPEG leases and all compressed bytes match; PCM decoding is separate.");
            foreach (string name in WaveformReferenceTestInput.Names)
            {
                (double maximum, double rms) = await WaveformReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Waveform"), name, 127);
                Console.WriteLine($"PASS waveform reference {name}: full PCM/counts/timing and all RMS/peak windows match declared independent bounds; max {maximum:G6}, RMS {rms:G6}.");
            }
            foreach (string name in AacDecodeTestInput.Names)
            {
                double difference = await AacDecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name);
                await AacFeatureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name);
                Console.WriteLine($"PASS AAC-LC {name}: complete PCM/timing/ownership and reference sample or noise energy checks; maximum PCM difference {difference:G9}.");
            }
            for (int sequence = 0; sequence < 4; sequence++)
            {
                for (int shape = 0; shape < 2; shape++)
                {
                    for (int previousShape = 0; previousShape < 2; previousShape++)
                    {
                        AacAnalysisTestInput.VerifyTransform(sequence, shape, previousShape);
                    }
                }
            }
            AacAnalysisTestInput.VerifyTransitions();
            AacWindowSelectionTestInput.VerifyDetector();
            await HighRateAacTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"));
            foreach (int rate in new[] { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000 })
            {
                foreach (int channels in new[] { 1, 2 })
                {
                    foreach (int samples in new[] { 1, 1023, 1024, 1025, 2048, 2051, 4133 })
                    {
                        AacAutomaticEncodingTestInput.Verify(rate, channels, samples);
                    }
                }
            }
            foreach (int offset in new[] { 0, 127, 128, 447, 448, 511, 575, 576, 1023 })
            {
                AacAutomaticEncodingTestInput.Verify(48000, 2, 4133, offset, 0);
                AacAutomaticEncodingTestInput.Verify(48000, 2, 4133, offset, 1);
            }
            for (int shape = 0; shape < 2; shape++)
            {
                for (int previousShape = 0; previousShape < 2; previousShape++)
                {
                    AacWindowSelectionTestInput.VerifyGrouping(1, shape, previousShape);
                    AacWindowSelectionTestInput.VerifyGrouping(2, shape, previousShape);
                }
            }
            for (int pattern = 0; pattern < 64; pattern++)
            {
                AacWindowSelectionTestInput.VerifyDecisions(pattern);
            }
            Console.WriteLine("PASS AAC lookahead: every six-block attack pattern preserves legal transitions and all unquantized samples, including initial short windows.");
            Console.WriteLine("PASS AAC analysis: long/start/short/stop direct coefficients, sine/KBD transitions, complete reconstruction and workspace bounds.");
            for (int frequency = 0; frequency < 12; frequency++)
            {
                AacWindowGroupingTestInput.VerifyFrequency(frequency);
                AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, false, 1);
                AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, false, 2);
                AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, true, 1);
                AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, true, 2);
            }
            AacWindowGroupingTestInput.VerifyAllocation(0, 0, 1, 1400);
            AacWindowGroupingTestInput.VerifyAllocation(3, 1, 2, 4000);
            AacWindowGroupingTestInput.VerifyAllocation(6, 2, 2, 3200);
            AacWindowGroupingTestInput.VerifyAllocation(11, 3, 1, 2500);
            Console.WriteLine("PASS AAC short groups: every sample rate, grouped order, scale history, exact sections and mono/stereo element budgets.");
            await AacEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-mono-48", 64000, 19);
            AacEncodingQualityTestInput.Verify(0);
            AacEncodingQualityTestInput.Verify(1);
            AacBandAllocationTestInput.VerifySharedBudget(3, 2400, 0, 0);
            AacBandAllocationTestInput.VerifySharedBudget(4, 3200, 1, 0);
            AacBandAllocationTestInput.VerifySharedBudget(3, 2400, 0, 0.05);
            AacBandAllocationTestInput.VerifySharedBudget(11, 12000, 1, 0);
            Console.WriteLine("PASS AAC shared channel budget: asymmetric spectra, exact channel syntax, silent output and CPE capacity.");
            await AacEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-stereo-44", 128000, 17);
            await AacEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-pns-48", 192000, 19);
            await AacEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "channel-budget", 96000, 107);
            await AacSwitchedEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), 1);
            await AacSwitchedEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), 2);
            Console.WriteLine("PASS AAC switched-window encoding: mono/stereo, every sine/KBD transition, grouped spectrum, PCM sample, budget and timestamp match independent decoding.");
            Console.WriteLine("PASS AAC encoding: every decoded sample in complete mono/stereo references matches, preserving priming, padding, timing and packet ownership.");
            await AacWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-mono-48");
            await AacWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-stereo-44");
            foreach (string name in new[] { "lc-mono-48", "lc-stereo-44" })
            {
                await AacEditedAudioTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name, false);
                await AacEditedAudioTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name, true);
                await AacWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name, true);
            }
            Console.WriteLine("PASS edited AAC: leading-delay removal, exact edit selection and trimmed 400 Hz waveform match independent PCM, timing and ownership.");
            Console.WriteLine("PASS AAC waveform: explicit mono mix, complete 400 Hz PCM16, exact timing, RMS and peak match processing of independent decoded references.");
            await Mp4MetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));
            foreach (string file in new[] { "avc-mp3-44.mp4", "avc-mp3-22.mp4" })
            {
                await MpegTsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);
                await Mp3HlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);
            }
            Console.WriteLine("PASS MP3 in MP4/TS/HLS: MPEG-1/2 frames, timestamps, transport tables and repeatable segments match.");
            Console.WriteLine("PASS MP4 metadata: Unicode tags, indexed keys, exact PNG/JPEG covers, unchanged packets and independent cover ownership match.");
            await NativeAudioProcessingTests.RunAsync();
            for (int index = 0; index < MediaProbeTestInput.Cases.Length; index++)
            {
                await MediaProbeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), index);
            }
            Console.WriteLine("PASS media probe: declared durations, codec IDs and owned snapshots match header references without decoders.");
            foreach (string file in Mp4IndexInfoTestInput.Names)
            {
                await Mp4IndexInfoTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);
            }
            Console.WriteLine("PASS MP4 index metadata: packet counts, payload bytes and coded durations match independent packets in both timeline modes.");
            await VideoStreamMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS video header metadata: all opening SPS descriptions, 8K, HDR, sampling and field declarations match independent references without picture allocation.");
            await AudioStreamMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS audio header metadata: PCM, AIFF, FLAC, MP3 and AAC sample parameters match independent references without decoders.");
            await MediaTagTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS header text tags: MP3 ID3, FLAC Vorbis and MP4 native keys match independent references after source disposal.");
            foreach (string name in WavePcmTestInput.Names)
            {
                await WavePcmTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Wave"), name);
            }
            Console.WriteLine("PASS WAVE PCM: integer/float samples, six-channel assignment, durations, public audio timestamps and independent frame ownership match.");
            foreach (string name in AiffPcmReferenceTestInput.Names)
            {
                await AiffPcmReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aiff"), name);
            }
            Console.WriteLine("PASS AIFF PCM: signed integer/float samples, both byte orders, durations, public timestamps and independent frame ownership match.");
            H264CabacTestInput.VerifyMixedBins();
            Console.WriteLine("PASS H.264 CABAC arithmetic: mixed decision, bypass and termination bins match exact intervals.");
            (string Name, string Extension, int Packet)[] cases =
                [("avc-tail", ".mov", 0), ("avc-tail", ".mov", 1), ("avc-tail", ".mov", 2),
                    ("avc-tail", ".mov", 3), ("avc-tail", ".mov", 4), ("avc-tail", ".mov", 5), ("avc-cropped", ".mp4", 0)];
            MediaBufferAllocator allocator = new(1024 * 1024);
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
            string h264Root = Path.Combine(AppContext.BaseDirectory, "Data", "H264");
            await NativeVideoEncodingTests.RunAsync(root, h264Root);
            await H264CabacBidirectionalTestInput.VerifyAsync(h264Root, root);
            await VideoSeekTestInput.VerifyAsync(root, "avc-multislice-cavlc", 18, 96, 80, 6);
            await VideoSeekTestInput.VerifyAsync(root, "avc-multislice-cabac", 18, 96, 80, 6);
            await VideoSeekTestInput.VerifyAsync(root, "avc-multislice-cabac-sliced", 18, 96, 80, 6);
            await VideoSeekTestInput.VerifyAsync(root, "avc-segments", 36, 64, 48, 5);
            await VideoSeekTestInput.VerifyAsync(root, "avc-aac", 8, 64, 48, 5);
            await VideoSeekTestInput.VerifyAsync(root, "avc-weighted", 12, 94, 62, 10);
            Console.WriteLine("PASS indexed video seek: forward/backward targets, EOF restart, exact YUV and retained frame ownership.");
            await H264MultiSliceTestInput.VerifyAsync(root, "avc-multislice-cavlc", 5);
            await H264MultiSliceTestInput.VerifyAsync(root, "avc-multislice-cabac", 5);
            await H264MultiSliceTestInput.VerifyAsync(root, "avc-multislice-cabac-sliced", 3);
            await H264BidirectionalTestInput.VerifyHeadersAsync(root);
            await H264BidirectionalTestInput.VerifyReferenceListsAsync(root);
            H264BidirectionalTestInput.VerifyMotion();
            H264BidirectionalDeblockingTestInput.Verify();
            H264SliceDeblockingTestInput.Verify();
            foreach (string name in new[] { "avc-cavlc-b-spatial", "avc-cavlc-b-temporal", "avc-aac", "avc-segments",
                "avc-cabac-b-0", "avc-cabac-b-1", "avc-cabac-b-2" })
            {
                await H264BidirectionalPictureTestInput.VerifyPicturesAsync(root, name);
                await H264BidirectionalPictureTestInput.VerifySessionAsync(root, name);
            }
            foreach (string name in new[] { "avc-cavlc-b-spatial", "avc-cavlc-b-temporal", "avc-cabac-b-0", "avc-cabac-b-1", "avc-cabac-b-2" })
            {
                await H264BidirectionalPictureTestInput.VerifyPreviewAsync(root, name);
            }
            Console.WriteLine("PASS H.264 CABAC B: 98 independent YUV pictures, all initializations, 8x8 transforms, direct modes, public timing and selected PNG previews match.");
            Console.WriteLine("PASS H.264 CAVLC B: 36 independent YUV pictures, spatial/temporal direct, display order, selected B previews and decoded PNG samples match.");
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
            foreach (string file in new[] { "avc-aac.mp4", "avc-tail.mov", "avc-timecode.mov", "avc-sound-v2.mov" })
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
