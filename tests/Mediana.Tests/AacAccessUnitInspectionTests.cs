// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacAccessUnitInspectionTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task StatelessInspectionFindsActualHeadersAndValidatesCompleteAccessUnits(int channels)
        {
            byte[][] packets = await AacSbrDecodeTestInput.ReadPacketsAsync(Root, channels);
            AacChannelLayout layout = new(channels);
            for (int index = 0; index < packets.Length; index++)
            {
                AacAccessUnitInspection result = Inspect(packets[index], layout);
                Assert.True(result.HasSbr);
                Assert.Equal(index is 0 or 11, result.Independent);
                byte[] truncated = packets[index][..^1];
                Assert.Throws<InvalidDataException>(() => Inspect(truncated, layout));
            }
            Assert.Throws<OperationCanceledException>(() => AacAccessUnitParser.Inspect(packets[0], 6, 48000, layout, new(true)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task HeaderSearchDoesNotAllocateDecoderWorkingBuffers(int channels)
        {
            byte[][] packets = await AacSbrDecodeTestInput.ReadPacketsAsync(Root, channels);
            AacChannelLayout layout = new(channels);
            Assert.False(Inspect(packets[1], layout).Independent);
            Assert.True(Inspect(packets[11], layout).Independent);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool unexpectedAnchor = false;
            const int inspectedPackets = 64;
            for (int index = 0; index < inspectedPackets; index++)
            {
                unexpectedAnchor |= Inspect(packets[1], layout).Independent;
            }
            AacAccessUnitInspection result = Inspect(packets[11], layout);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(unexpectedAnchor);
            Assert.True(result.Independent && result.HasSbr);
            Assert.True(allocated < 64 * 1024, $"Inspecting {inspectedPackets + 1} AAC packets allocated {allocated} bytes.");
        }

        private static AacAccessUnitInspection Inspect(byte[] packet, AacChannelLayout layout) =>
            AacAccessUnitParser.Inspect(packet, 6, 48000, layout, CancellationToken.None);

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr");
    }
}
