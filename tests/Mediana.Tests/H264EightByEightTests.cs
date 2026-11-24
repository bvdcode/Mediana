// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;
using System.Runtime.InteropServices;

namespace Mediana.Tests
{
    public class H264EightByEightTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(14)]
        public void DcOnlyBlockMatchesFullReconstructionAtEveryQuantizer(int depth)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 2, HeightInMapUnits = 2, FrameMbsOnly = true,
                LumaBitDepth = depth, ChromaBitDepth = depth, ChromaFormat = 3
            };
            MediaBufferAllocator allocator = new(65536);
            using H264Picture expected = new(sequence, allocator, H264SliceKind.I);
            using H264Picture actual = new(sequence, allocator, H264SliceKind.I);
            byte[] strided = new byte[16 * 8 * actual.SampleBytes];
            byte[] tight = new byte[8 * 8 * actual.SampleBytes];
            Random random = new(6031);
            random.NextBytes(strided);
            if (depth > 8)
            {
                Span<ushort> samples = MemoryMarshal.Cast<byte, ushort>(strided);
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] &= (ushort)actual.MaximumSample;
                }
            }
            H264BlockRows.Copy(strided, 16 * actual.SampleBytes, tight, 8 * actual.SampleBytes, 8 * actual.SampleBytes, 8);
            int[] coefficients = new int[64];
            int[] residual = new int[64];
            ReadOnlySpan<int> levels = [-4096, -33, -1, 1, 33, 4096];
            for (int qp = 0; qp <= actual.LumaQuantizer.Maximum; qp++)
            {
                foreach (int level in levels)
                {
                    coefficients[0] = level;
                    H264InverseTransform8x8.Residual(coefficients, qp, residual, depth);
                    expected.WriteLuma8x8(8, 16, tight, 8, residual, 1);
                    actual.WriteConstant8x8(1, 8, 16, strided, 16, H264InverseTransform8x8.DcResidual(level, qp));
                    Assert.True(expected.ReadPlane(1).SequenceEqual(actual.ReadPlane(1)));
                }
            }
        }

        [Fact]
        public void StridedSimplePredictionAddsSignedDcOnlyInsideBlock()
        {
            ushort[] top = [200, 204, 208, 212, 216, 220, 224, 228, 252, 252, 252, 252, 252, 252, 252, 252];
            ushort[] left = [400, 392, 384, 376, 368, 360, 352, 344];
            int[] filteredTop = [201, 204, 208, 212, 216, 220, 224, 233];
            int[] filteredLeft = [348, 392, 384, 376, 368, 360, 352, 346];
            const int mean = 290;
            const int stride = 19;
            const int origin = 3;
            ushort[] pixels = new ushort[stride * 9];
            for (int mode = 0; mode < 3; mode++)
            {
                foreach (int delta in new[] { int.MinValue, -209, 0, 670, int.MaxValue })
                {
                    Array.Fill(pixels, ushort.MaxValue);
                    H264IntraPrediction<ushort>.WriteSimple8x8(mode, top, left, 200, pixels.AsSpan(origin), stride, 1023, delta);
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        int x = index % stride - origin;
                        int y = index / stride;
                        long expected = ushort.MaxValue;
                        if (x >= 0 && x < 8 && y < 8)
                        {
                            int prediction = mode switch
                            {
                                0 => filteredTop[x],
                                1 => filteredLeft[y],
                                2 => mean,
                                _ => throw new InvalidOperationException()
                            };
                            expected = Math.Clamp((long)prediction + delta, 0, 1023);
                        }
                        Assert.Equal((ushort)expected, pixels[index]);
                    }
                }
            }
        }

        [Fact]
        public async Task AllDirectionalModesMatchEveryIndependentPredictedSample() =>
            await H264EightByEightTestInput.VerifyPredictionVectorsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));

        [Fact]
        public async Task EveryExplicitIntraModeAlsoMatchesThroughCompletePictureReconstruction() =>
            await H264EightByEightTestInput.VerifyExplicitPicturesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));

        [Theory]
        [InlineData("avc-cavlc-8x8", 18, null)]
        [InlineData("avc-cavlc-8x8-weighted", 12, null)]
        [InlineData("avc-cabac-8x8-0", 18, 0)]
        [InlineData("avc-cabac-8x8-1", 12, 1)]
        [InlineData("avc-cabac-8x8-2", 12, 2)]
        public async Task CompleteHighProfileSequenceExercisesIntraAndInter8x8AndMatchesEveryYuvSample(string name, int count, int? initialization) =>
            await H264EightByEightTestInput.VerifySequenceAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, count, initialization);

        [Fact]
        public void CavlcResidualGroupsInterleaveAcrossTheComplete8x8Scan()
        {
            H264SequenceParameterSet sequence = new() { WidthInMbs = 1, HeightInMapUnits = 1, Width = 16, Height = 16, FrameMbsOnly = true };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(sequence, allocator, H264SliceKind.P);
            H264BitReader bits = new(H264TestBits.Rbsp("01 0 1 01 1 011 01 0 010 01 1 000000001"));
            int[] coefficients = new int[64];
            H264CavlcResidualReader reader = new(picture);
            Assert.Equal(4, reader.Read(ref bits, H264ResidualCategory.Luma8x8, 0, 0, 0, coefficients));
            Assert.Equal(1, coefficients[0]);
            Assert.Equal(-1, coefficients[5]);
            Assert.Equal(1, coefficients[10]);
            Assert.Equal(-1, coefficients[63]);
            Assert.Equal(4, coefficients.Count(value => value != 0));
            foreach ((int x, int y) in new[] { (0, 0), (4, 0), (0, 4), (4, 4) })
            {
                Assert.Equal(1, picture.Counts(0)[picture.BlockIndex(0, x, y)]);
            }
            bits.ReadTrailingBits();
        }

        [Theory]
        [InlineData("avc-cavlc-8x8")]
        [InlineData("avc-cabac-8x8-0")]
        public async Task TruncatedOrCancelled8x8PicturesReleaseStorageAndPreservePreviousReferences(string name)
        {
            (H264Configuration configuration, byte[] first) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor(name + ".mp4"), 0);
            (_, byte[] next) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor(name + ".mp4"), 1);
            byte[] expected = await Mp4TestData.ReadAsync(name + ".sequence.yuv");
            const int frameBytes = 190 * 126 * 3 / 2;
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                H264PictureOrderState order = new();
                H264TestInput.VerifyAndStorePicture(first, configuration, references, ref order, allocator, expected.AsSpan(0, frameBytes));
                H264BitReader headerBits = H264Nal.Payload(next, H264Nal.Type(next));
                H264SliceHeader header = H264SliceHeaderReader.Read(ref headerBits, configuration, next[0]);
                IReadOnlyList<H264ReferencePicture?> list = references.GetList0(header);
                long retained = allocator.AllocatedBytes;
                foreach (int length in new[] { next.Length / 3, next.Length / 2, next.Length - 8 })
                {
                    Assert.Throws<InvalidDataException>(() => Decode(next[..length], configuration, list, allocator));
                    Assert.Equal(retained, allocator.AllocatedBytes);
                }
                Assert.Throws<OperationCanceledException>(() => Decode(next, configuration, list, allocator, new CancellationToken(true)));
                Assert.Equal(retained, allocator.AllocatedBytes);
                H264TestInput.VerifyAndStorePicture(next, configuration, references, ref order, allocator, expected.AsSpan(frameBytes, frameBytes));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void Decode(byte[] nal, H264Configuration configuration,
            IReadOnlyList<H264ReferencePicture?> references, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            using H264Picture picture = H264PictureDecoder.Decode(ref bits, header, references, allocator, cancellationToken);
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(1, 6)]
        [InlineData(2, 7)]
        [InlineData(3, 7)]
        [InlineData(4, 8)]
        [InlineData(5, 9)]
        [InlineData(6, 10)]
        [InlineData(35, 288)]
        [InlineData(36, 320)]
        [InlineData(51, 1792)]
        public void ConstantFrequencyMatchesNormativeScaleAndRounding(int qp, int expected)
        {
            int[] coefficients = new int[64];
            coefficients[0] = 64;
            int[] residual = new int[64];
            H264InverseTransform8x8.Residual(coefficients, qp, residual);
            Assert.All(residual, value => Assert.Equal(expected, value));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(-1, false)]
        [InlineData(1, true)]
        [InlineData(-1, true)]
        public void OddFrequencyPreservesOrientationAndSignedRounding(int sign, bool vertical)
        {
            int[] coefficients = new int[64];
            coefficients[vertical ? 2 : 1] = sign * 64;
            int[] residual = new int[64];
            H264InverseTransform8x8.Residual(coefficients, 0, residual);
            int[] line = [7, 6, 4, 2, -2, -4, -6, -7];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal(sign * line[vertical ? y : x], residual[y * 8 + x]);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void DirectionalPredictionRejectsMissingNeighbours(int mode) =>
            Assert.Throws<InvalidDataException>(() => H264IntraPrediction.Luma8x8(mode, [], [], null, new byte[64]));

        [Fact]
        public void FilteredDcHandlesAbsentSidesAndCorner()
        {
            byte[] output = new byte[64];
            H264IntraPrediction.Luma8x8(2, [], [], null, output);
            Assert.All(output, value => Assert.Equal(128, value));
            H264IntraPrediction.Luma8x8(2, [], [0, 0, 0, 0, 0, 0, 0, 255], null, output);
            Assert.All(output, value => Assert.Equal(32, value));
            H264IntraPrediction.Luma8x8(2, [0, 0, 0, 0, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0], [], null, output);
            Assert.All(output, value => Assert.Equal(24, value));
        }
    }
}
