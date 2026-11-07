// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltSynthesis
    {
        /// <summary>Reuses planar frequency storage for stereo downmixing and preserves each output channel's incoming overlap.</summary>
        public static void Apply(CeltTransformPlan plan, CeltBandMode mode, bool transient, int outputChannels,
            Span<float> frequency, Span<float> output, int stride, Span<float> workspace)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            if (outputChannels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(outputChannels)); }
            int samples = CeltBandTables.ShortTransformSize << mode.FrameShift, tail = CeltTransformPlan.Overlap / 2;
            ArgumentOutOfRangeException.ThrowIfLessThan(frequency.Length, samples * mode.Channels, nameof(frequency));
            ArgumentOutOfRangeException.ThrowIfLessThan(stride, samples + tail);
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, (outputChannels - 1) * stride + samples + tail, nameof(output));
            ArgumentOutOfRangeException.ThrowIfLessThan(workspace.Length, samples, nameof(workspace));
            if (frequency.Overlaps(output) || frequency.Overlaps(workspace) || output.Overlaps(workspace))
            {
                throw new ArgumentException("CELT synthesis frequency, output and workspace must not overlap.");
            }
            if (mode.Channels == 2 && outputChannels == 1)
            {
                for (int at = 0; at < samples; at++) { frequency[at] = .5f * frequency[at] + .5f * frequency[samples + at]; }
            }
            int blocks = 1, shift = mode.FrameShift, blockSize = samples;
            if (transient) { blocks = 1 << mode.FrameShift; shift = 0; blockSize = CeltBandTables.ShortTransformSize; }
            for (int channel = 0; channel < outputChannels; channel++)
            {
                int sourceChannel = 0;
                if (mode.Channels == 2 && outputChannels == 2) { sourceChannel = channel; }
                ReadOnlySpan<float> source = frequency.Slice(sourceChannel * samples, samples);
                for (int block = 0; block < blocks; block++)
                {
                    CeltImdct.Transform(plan, source[block..], output[(channel * stride + block * blockSize)..],
                        workspace, shift, blocks);
                }
            }
        }
    }
}
