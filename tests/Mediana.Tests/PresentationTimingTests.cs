// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;

namespace Mediana.Tests
{
    public class PresentationTimingTests
    {
        [Theory]
        [InlineData("frame-rate-cfr.mp4")]
        [InlineData("frame-rate-vfr.mp4")]
        [InlineData("frame-rate-edited.mp4")]
        [InlineData("preview-hd.mp4")]
        public async Task ExactPresentationIntervalsPreserveRawPacketDurationsAndCursors(string name)
        {
            foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
            {
                await PresentationTimingTestInput.VerifyReferenceAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name, mode);
            }
        }

        [Fact]
        public Task FailedIndexConstructionReleasesTemporaryStorageAndAllowsRetry() =>
            PresentationTimingTestInput.VerifyLimitsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));

        [Theory]
        [InlineData("frame-rate-cfr.mp4")]
        [InlineData("frame-rate-vfr.mp4")]
        [InlineData("frame-rate-edited.mp4")]
        public async Task JointAndIndependentDecodingReturnIdenticalPresentationIntervals(string name)
        {
            foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
            {
                await PresentationTimingTestInput.VerifyJointDecodingAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name, mode);
            }
        }

        [Fact]
        public Task SignedCompositionOffsetsPreserveIntervalsAndDuplicatesRemainUnknown() =>
            PresentationTimingTestInput.VerifySignedAndUnknownAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));

        [Fact]
        public void CorrectedPacketsRetainPayloadWithoutCopyingOrMutatingOriginalTiming() => PresentationTimingTestInput.VerifyRetainedPacket();
    }
}
