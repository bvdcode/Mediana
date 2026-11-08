// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacPsOriginTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InitialEmptyStereoEnvelopeIsAStreamOriginButNotAnArbitraryRestart(bool explicitPs)
        {
            byte[][] packets = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            MediaBufferAllocator allocator = new(256 * 1024);
            IRandomAccessInspector inspector = new AacDecoderFactory().CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(explicitPs));
            IRandomAccessOriginInspector origin = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(inspector);
            Assert.True(origin.RequiresOriginPacket);
            using MediaPacket first = AacSbrDecodeTestInput.Packet(allocator, packets[0], 0);
            Assert.False(inspector.IsRandomAccess(first));
            Assert.Equal(2, origin.InspectOrigin(first)?.RequiredPrerollPackets);
            Assert.False(origin.RequiresOriginPacket);
            Assert.False(inspector.IsRandomAccess(first));
        }

        [Fact]
        public async Task MissingInitialSbrHeaderHasNoStreamOriginGuarantee()
        {
            byte[][] packets = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            MediaBufferAllocator allocator = new(256 * 1024);
            IRandomAccessOriginInspector origin = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(
                new AacDecoderFactory().CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(true)));
            using MediaPacket packet = AacSbrDecodeTestInput.Packet(allocator, packets[1], 1);
            Assert.Null(origin.InspectOrigin(packet));
        }

        [Fact]
        public async Task OpeningStereoSignalingSurvivesPacketDisposalAndConstrainsLaterAnchors()
        {
            byte[][] stereo = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            byte[][] mono = await AacSbrDecodeTestInput.ReadPacketsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr"), 1);
            MediaBufferAllocator allocator = new(256 * 1024);
            IRandomAccessInspector inspector = new AacDecoderFactory().CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(false));
            IRandomAccessOriginInspector origin = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(inspector);
            Assert.True(origin.RequiresOriginPacket);
            using (MediaPacket first = AacSbrDecodeTestInput.Packet(allocator, stereo[0], 0))
            {
                origin.InspectOrigin(first);
                Assert.False(origin.RequiresOriginPacket);
                Assert.Equal(first.Data.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            using (MediaPacket candidate = AacSbrDecodeTestInput.Packet(allocator, mono[0], 0))
            {
                Assert.False(inspector.IsRandomAccess(candidate));
            }
            using (MediaPacket candidate = AacSbrDecodeTestInput.Packet(allocator, stereo[10], 10))
            {
                Assert.True(inspector.IsRandomAccess(candidate));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task AbsenceAtOriginDoesNotSuppressLaterPositiveStereoDetection()
        {
            byte[][] stereo = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            byte[][] mono = await AacSbrDecodeTestInput.ReadPacketsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr"), 1);
            MediaBufferAllocator allocator = new(256 * 1024);
            IRandomAccessInspector inspector = new AacDecoderFactory().CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(false));
            IRandomAccessOriginInspector origin = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(inspector);
            using (MediaPacket first = AacSbrDecodeTestInput.Packet(allocator, mono[0], 0))
            {
                origin.InspectOrigin(first);
                Assert.True(inspector.IsRandomAccess(first));
            }
            using (MediaPacket candidate = AacSbrDecodeTestInput.Packet(allocator, stereo[10], 10))
            {
                Assert.True(inspector.IsRandomAccess(candidate));
            }
            using (MediaPacket candidate = AacSbrDecodeTestInput.Packet(allocator, mono[0], 0))
            {
                Assert.False(inspector.IsRandomAccess(candidate));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAndInvalidSyntaxDoNotCompleteOriginInspection()
        {
            byte[][] stereo = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            MediaBufferAllocator allocator = new(256 * 1024);
            IRandomAccessOriginInspector inspector = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(
                new AacDecoderFactory().CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(false)));
            using (MediaPacket first = AacSbrDecodeTestInput.Packet(allocator, stereo[0], 0))
            using (CancellationTokenSource cancellation = new())
            {
                await cancellation.CancelAsync();
                Assert.Throws<OperationCanceledException>(() => inspector.InspectOrigin(first, cancellation.Token));
                Assert.True(inspector.RequiresOriginPacket);
            }
            using (MediaPacket malformed = AacSbrDecodeTestInput.Packet(allocator, [0], 0))
            {
                Assert.Throws<InvalidDataException>(() => inspector.InspectOrigin(malformed));
                Assert.True(inspector.RequiresOriginPacket);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExplicitPsRequiresOriginInitializationButNonMonauralStreamsDoNot()
        {
            AacDecoderFactory factory = new();
            IRandomAccessOriginInspector explicitPs = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(
                factory.CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(true)));
            IRandomAccessOriginInspector stereo = Assert.IsAssignableFrom<IRandomAccessOriginInspector>(
                factory.CreateRandomAccessInspector(AacSbrDecodeTestInput.Stream(2, false)));
            Assert.True(explicitPs.RequiresOriginPacket);
            Assert.False(stereo.RequiresOriginPacket);
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "AacPs");
    }
}
