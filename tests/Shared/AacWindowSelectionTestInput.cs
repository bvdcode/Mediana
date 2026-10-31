// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class AacWindowSelectionTestInput
    {
        public static void VerifyDecisions(int pattern)
        {
            const int Frames = 11;
            AacWindowSelector selector = new();
            double[] input = new double[2048];
            double[] spectrum = new double[1024];
            double[] window = new double[2048];
            double[] folded = new double[1024];
            double[] output = new double[1024];
            double[] overlap = new double[1024];
            double[] time = new double[2048];
            Complex[] work = new Complex[512];
            int previous = -1;
            for (int frame = 0; frame < Frames; frame++)
            {
                bool attack = frame < 6 && (pattern & (1 << frame)) != 0;
                bool nextAttack = frame + 1 < 6 && (pattern & (1 << (frame + 1))) != 0;
                int sequence = selector.Select(attack, nextAttack);
                bool precedingAttack = frame > 0 && frame <= 6 && (pattern & (1 << (frame - 1))) != 0;
                if ((attack || precedingAttack) && sequence != 2)
                {
                    throw new InvalidDataException("AAC did not protect both analysis windows containing an attack.");
                }
                bool legal = previous switch
                {
                    -1 => sequence is 0 or 1 or 2,
                    0 or 3 => sequence is 0 or 1,
                    1 => sequence is 2 or 3,
                    2 => sequence is 2 or 3,
                    _ => false
                };
                if (!legal || (frame == Frames - 1 && sequence != 0))
                {
                    throw new InvalidDataException("AAC selected an invalid transition or failed to return to long windows.");
                }
                for (int sample = 0; sample < input.Length; sample++)
                {
                    int position = (frame - 1) * 1024 + sample;
                    input[sample] = position >= 0 && position < (Frames - 1) * 1024 ? Signal(position) * 32768 : 0;
                }
                AacAnalysis.Transform(input, sequence, 0, 0, spectrum, window, folded, work, default);
                AacSynthesis.Process(spectrum, sequence, 0, 0, overlap, output, time, work, default);
                for (int sample = 0; sample < output.Length; sample++)
                {
                    double expected = frame == 0 ? 0 : Signal((frame - 1) * 1024 + sample);
                    if (!double.IsFinite(output[sample]) || Math.Abs(expected - output[sample]) > 2e-12)
                    {
                        throw new InvalidDataException("AAC lookahead or an initial short window changed an unquantized sample.");
                    }
                }
                previous = sequence;
            }
        }

        public static void VerifyDetector()
        {
            double[] block = new double[1024];
            AacTransientDetector detector = new();
            if (detector.Analyze(block, default))
            {
                throw new InvalidDataException("AAC detected a transient in digital silence.");
            }
            foreach (int offset in new[] { 0, 127, 128, 447, 448, 511, 575, 576, 1023 })
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    detector = new();
                    block.AsSpan().Clear();
                    block[offset] = sign * 30000;
                    if (!detector.Analyze(block, default))
                    {
                        throw new InvalidDataException("AAC missed an isolated transient at a short-window boundary.");
                    }
                    block.AsSpan().Clear();
                    if (detector.Analyze(block, default))
                    {
                        throw new InvalidDataException("AAC detected an attack in a decaying filter tail.");
                    }
                }
            }
            detector = new();
            for (int frame = 0; frame < 8; frame++)
            {
                for (int sample = 0; sample < block.Length; sample++)
                {
                    block[sample] = 20000 * Math.Sin((frame * 1024 + sample) * Math.PI / 4);
                }
                bool transient = detector.Analyze(block, default);
                if (frame > 0 && transient)
                {
                    throw new InvalidDataException("AAC repeatedly detected a transient in a stationary high-frequency tone.");
                }
            }
        }

        public static void VerifyGrouping(int channels, int shape, int previousShape)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (AacEncoderWorkspace workspace = new(allocator, channels))
            {
                int[] groups = new int[9];
                groups[^1] = 37;
                int count = AacShortWindowGrouping.Create(workspace, shape, previousShape, groups.AsSpan(0, 8));
                if (count != 1 || groups[0] != 8)
                {
                    throw new InvalidDataException("AAC did not group silent short windows.");
                }
                for (int channel = 0; channel < channels; channel++)
                {
                    workspace.Input(channel).Fill(1);
                }
                count = AacShortWindowGrouping.Create(workspace, shape, previousShape, groups.AsSpan(0, 8));
                if (count != 1 || groups[0] != 8)
                {
                    throw new InvalidDataException("AAC fragmented constant-energy short windows.");
                }
                Span<double> transient = workspace.Input(channels - 1);
                transient.Clear();
                transient[1024] = 0.00001;
                count = AacShortWindowGrouping.Create(workspace, shape, previousShape, groups.AsSpan(0, 8));
                if (count != 3 || !groups.AsSpan(0, count).SequenceEqual([3, 2, 3]) ||
                    transient[1024] != 0.00001 || groups[^1] != 37)
                {
                    throw new InvalidDataException("AAC grouped an isolated quiet-channel attack with silent windows or exceeded storage.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("AAC grouping retained analysis storage.");
            }
        }

        private static double Signal(int sample) => 0.3 * Math.Sin(sample * 0.019) + 0.1 * Math.Cos(sample * 0.171)
            + (sample % 1024 is 0 or 447 or 448 or 575 or 576 or 1023 ? 0.2 : 0);
    }
}
