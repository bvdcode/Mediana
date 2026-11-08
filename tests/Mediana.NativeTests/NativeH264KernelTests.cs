// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeH264KernelTests
    {
        public static async Task RunAsync()
        {
            H264ZeroResidualTestInput.VerifyMacroblocks();
            H264ResidualRegionTestInput.Verify();
            H264CabacDecisionTestInput.Verify();
            H264DeblockingFilterTestInput.Verify();
            H264DeblockingFilterTestInput.VerifyAllocation();
            H264BidirectionalDeblockingTestInput.Verify();
            H264DeblockingMotionTestInput.Verify();
            H264MotionLookupTestInput.Verify();
            H264UniformPredictionTestInput.Verify();
            H264UniformContextTestInput.Verify();
            H264UniformRangeTestInput.Verify();
            H264ForwardTransformTestInput.Verify();
            H264ForwardTransformTestInput.VerifyAllocation();
            H264ForwardQuantizationTestInput.Verify();
            H264BlockPairTestInput.Verify();
            H264BitReadingTestInput.Verify();
            H264VlcTestInput.Verify();
            H264VlcWritingTestInput.Verify();
            H264CavlcLevelTestInput.Verify();
            H264DcResidualTestInput.VerifyDcOnly();
            H264DcResidualTestInput.VerifyAcBlocks();
            H264DcResidualTestInput.VerifyOverlappingStorage();
            H264VectorKernelTestInput.Verify();
            H264MotionInterpolationTestInput.Verify();
            H264LumaNeighborhoodTestInput.Verify();
            H264ShortInterpolationTestInput.Verify();
            H264SampleDifferenceTestInput.Verify();
            H264LumaCandidateTestInput.Verify();
            H264LumaNeighborhoodTestInput.VerifyAllocation();
            await H264MotionSearchTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
            H264ChromaInterpolationTestInput.Verify();
            H264IntraModeTestInput.Verify();
            foreach (int qp in new[] { 0, 12, 26, 38, 51 })
            {
                (byte[] _, byte[] pixels) = await H264Intra16TestInput.EncodeAsync(qp);
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", $"intra16-qp{qp}.yuv"));
                if (!pixels.AsSpan().SequenceEqual(reference))
                {
                    throw new InvalidDataException("H.264 intra16 coding differs from the independent complete pixel reference.");
                }
            }
            Console.WriteLine("PASS H.264 motion interpolation: all luma phases, complete blocks, borders and extreme coordinates match scalar convolution.");
            Console.WriteLine("PASS H.264 intra16 coding: I/P syntax, every prediction mode, five quantizers and complete independent samples match.");
        }
    }
}
