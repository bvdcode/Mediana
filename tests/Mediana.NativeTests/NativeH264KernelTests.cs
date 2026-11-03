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
            H264DeblockingFilterTestInput.Verify();
            H264DeblockingFilterTestInput.VerifyAllocation();
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
            Console.WriteLine("PASS H.264 motion interpolation: all luma phases, complete blocks, borders and extreme coordinates match scalar convolution.");
        }
    }
}
