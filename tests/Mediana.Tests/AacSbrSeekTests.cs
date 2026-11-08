// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacSbrSeekTests
    {
        [Theory]
        [InlineData(1, false, false)]
        [InlineData(2, false, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(1, false, true)]
        [InlineData(2, false, true)]
        [InlineData(1, true, true)]
        [InlineData(2, true, true)]
        public async Task ColdAndRepeatedSeeksPreserveRequestedClocksAndIndependentRestartPcm(int channels, bool explicitSbr, bool mp4)
        {
            await AacSbrSeekTestInput.VerifyAsync(Root, channels, explicitSbr, mp4);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task InspectorRequiresActualSbrHeaderAndDoesNotRetainPacketState(int channels)
        {
            byte[][] packets = await AacSbrDecodeTestInput.ReadPacketsAsync(Root, channels);
            IRandomAccessRecoveryInspector inspector = Assert.IsAssignableFrom<IRandomAccessRecoveryInspector>(
                new AacDecoderFactory().CreateRandomAccessInspector(AacSbrDecodeTestInput.Stream(channels, false)));
            MediaBufferAllocator allocator = new(64 * 1024);
            foreach (int index in new[] { 15, 11, 1, 0, 11, 15 })
            {
                using MediaPacket packet = AacSbrDecodeTestInput.Packet(allocator, packets[index], index);
                RandomAccessRecovery? recovery = inspector.InspectRecovery(packet);
                Assert.Equal(index is 0 or 11, recovery.HasValue);
                if (recovery.HasValue)
                {
                    Assert.Equal(2, recovery.Value.RequiredPrerollPackets);
                }
                Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            }
            using MediaPacket truncated = AacSbrDecodeTestInput.Packet(allocator, packets[0][..^1], 0);
            Assert.Throws<InvalidDataException>(() => inspector.InspectRecovery(truncated));
            Assert.Throws<OperationCanceledException>(() => inspector.InspectRecovery(truncated, new(true)));
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr");
    }
}
