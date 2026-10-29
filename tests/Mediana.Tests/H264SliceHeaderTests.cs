// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264SliceHeaderTests
    {
        [Theory]
        [InlineData(0, 0, 0, 23, true)]
        [InlineData(1, 1, 1, 24, false)]
        [InlineData(2, 2, 2, 24, false)]
        [InlineData(3, 3, 3, 25, false)]
        [InlineData(4, 4, 3, 26, false)]
        [InlineData(5, 0, 0, 26, true)]
        public async Task RealBaselineHeadersMatchIndependentTrace(int packet, int frameNumber, int references, int qp, bool idr)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), packet);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            Assert.Equal(idr, header.IsIdr);
            Assert.Equal(frameNumber, header.FrameNumber);
            Assert.Equal(references, header.ActiveReferencesL0);
            Assert.Equal(qp, header.Qp);
            Assert.Equal(0, header.CabacInitIdc);
            Assert.Equal(idr ? H264SliceKind.I : H264SliceKind.P, header.Kind);
            Assert.Equal(0, header.FirstMacroblock);
            Assert.Equal(0, header.DeblockingIdc);
            Assert.Equal(0, header.AlphaOffset);
            Assert.Equal(0, header.BetaOffset);
            Assert.Empty(header.ReferenceModifications);
            Assert.Empty(header.MemoryOperations);
            Assert.False(header.AdaptiveMarking);
            Assert.Null(header.Weights);
            Assert.True(bits.MoreRbspData());
        }

        [Fact]
        public async Task TruncatedHeadersFailWithoutReadingBeyondTheNal()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), 4);
            for (int length = 1; length <= 3; length++)
            {
                byte[] truncated = nal[..length];
                Assert.Throws<InvalidDataException>(() => Read(truncated, configuration));
            }
        }

        private static void Read(byte[] nal, H264Configuration configuration)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
        }
    }
}
