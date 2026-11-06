// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkStereoSynthesisTests
    {
        [Fact]
        public void SequentialStereoUnmixingMatchesIndependentPcmAndHistory() =>
            Assert.Equal(1536, OpusSilkStereoTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void MonoDelayContinuesAcrossFramesAndResetRestoresSilence()
        {
            SilkStereoState state = new();
            short[] first = [1, 2, 3], second = [4, 5];
            SilkStereoSynthesis.DelayMono(state, first);
            SilkStereoSynthesis.DelayMono(state, second);
            Assert.Equal(new short[] { 0, 1, 2 }, first);
            Assert.Equal(new short[] { 3, 4 }, second);
            Assert.Equal(4, state.MidPrevious2); Assert.Equal(5, state.MidPrevious1);
            state.Reset();
            SilkStereoSynthesis.DelayMono(state, second);
            Assert.Equal(new short[] { 0, 3 }, second);
        }

        [Fact]
        public void StereoUnmixingReusesTheInputPlanesWithoutAllocating()
        {
            SilkStereoState state = new(); SilkFrameMode mode = new(16000, 4);
            Span<short> mid = stackalloc short[320], side = stackalloc short[320];
            mid.Fill(100); side.Fill(-100);
            SilkStereoPrediction prediction = new(2560, -1280);
            SilkStereoSynthesis.Unmix(state, mode, prediction, mid, side);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++) { SilkStereoSynthesis.Unmix(state, mode, prediction, mid, side); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
