using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BidirectionalDeblockingTests
    {
        [Fact]
        public void ReferenceIdentityListSwapsAndMotionThresholdsMatchBoundaryVectors() => H264BidirectionalDeblockingTestInput.Verify();

        [Fact]
        public void MissingL1ReferencesAreRejectedUnlessAnIntraOrResidualEdgeTakesPriority()
        {
            H264SequenceParameterSet sequence = new() { WidthInMbs = 2, HeightInMapUnits = 1, FrameMbsOnly = true };
            using H264Picture picture = new(sequence, new MediaBufferAllocator(2048), H264SliceKind.B);
            picture.SetMotion(H264ReferenceList.L1, 0, 0, 32, 16, 0, 0, 0);
            picture.Modes.Fill(H264Picture.InterMode);
            Assert.Throws<InvalidDataException>(() => H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, [], []));
            int index = picture.BlockIndex(0, 16, 0);
            picture.Counts(0)[index] = 1;
            Assert.Equal(2, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, [], []));
            picture.Modes[index] = 2;
            Assert.Equal(4, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, [], []));
        }
    }
}
