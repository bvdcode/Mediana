// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltRestorationTests
    {
        [Fact]
        public void AntiCollapseAndDenormalizationMatchTheIndependentDecoder() =>
            Assert.Equal(512, OpusRestorationTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void ReconstructionHonorsTheFloatingEnergyFloor()
        {
            Assert.Equal(0, CeltEnergyMath.Exp2(MathF.BitDecrement(-50)));
            Assert.Equal(MathF.ScaleB(1, -50), CeltEnergyMath.Exp2(-50));
        }

        [Fact]
        public void RepeatedRestorationUsesOnlyCallerStorage()
        {
            Span<float> energies = stackalloc float[42], previous = stackalloc float[42], previous2 = stackalloc float[42];
            Span<float> spectrum = stackalloc float[1920], frequency = stackalloc float[1920];
            Span<byte> masks = stackalloc byte[42];
            Span<int> shape = stackalloc int[21];
            energies.Clear(); previous.Clear(); previous2.Clear(); shape.Clear(); masks.Clear(); spectrum.Clear();
            CeltBandMode mode = new(2, 3, 0, 21);
            CeltAntiCollapse.Apply(mode, new(spectrum, default, masks), energies, previous, previous2, shape, 17);
            CeltBandDenormalizer.Apply(mode, spectrum, energies, frequency, false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++)
            {
                CeltAntiCollapse.Apply(mode, new(spectrum, default, masks), energies, previous, previous2, shape, 17);
                CeltBandDenormalizer.Apply(mode, spectrum, energies, frequency, false);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
    }
}
