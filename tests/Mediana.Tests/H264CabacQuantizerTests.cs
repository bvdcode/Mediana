// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacQuantizerTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(14)]
        public async Task EveryQuantizerDeltaMatchesUnaryIntervalsAndWrapsAtBothEnds(int bitDepth)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264BitReader input = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref input, configuration, nal[0]);
            H264Quantizer quantizer = new(bitDepth);
            int contextQp = header.Qp;
            header = header with { Sequence = header.Sequence with { LumaBitDepth = bitDepth }, Qp = quantizer.FromSyntax(contextQp) };
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-intra-contexts.bin"));
            H264CabacResidualWriter writer = new(contextQp, states);
            int[] deltas = Enumerable.Range(quantizer.MinimumDelta, quantizer.Maximum + 1).ToArray();
            bool previous = false;
            foreach (int delta in deltas)
            {
                writer.QuantizerDelta(delta, previous);
                previous = delta != 0;
            }
            writer.QuantizerDelta(0, false);
            H264BitReader bits = new(writer.Finish());
            H264CabacReader reader = new(ref bits, header);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture target = new(header.Sequence, allocator, header.Kind))
            using (H264CabacMacroblockState state = new(target, allocator))
            {
                H264CabacPictureSyntax syntax = new(reader, target, state);
                int qp = 0;
                foreach (int delta in deltas)
                {
                    int expected = (qp + delta + quantizer.Maximum + 1) % (quantizer.Maximum + 1);
                    qp = syntax.ReadQuantizer(ref bits, 1, 0, qp);
                    Assert.Equal(expected, qp);
                }
                Assert.Equal(qp, syntax.ReadQuantizer(ref bits, 0, 0, qp));
                Assert.Equal(qp, syntax.ReadQuantizer(ref bits, 1, 0, qp));
                Assert.True(reader.Termination(ref bits));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
