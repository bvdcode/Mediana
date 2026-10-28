using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacQuantizerTests
    {
        [Fact]
        public async Task EveryQuantizerDeltaMatchesUnaryIntervalsAndWrapsAtBothEnds()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264BitReader input = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref input, configuration, nal[0]);
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-intra-contexts.bin"));
            H264CabacResidualWriter writer = new(header.Qp, states);
            int[] deltas = Enumerable.Range(-26, 52).ToArray();
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
            using (H264Picture target = new(header.Sequence, allocator))
            using (H264CabacIntraState state = new(header.Sequence, allocator))
            {
                H264CabacIntraSyntax syntax = new(reader, target, state);
                int qp = 0;
                foreach (int delta in deltas)
                {
                    int expected = (qp + delta + 52) % 52;
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
