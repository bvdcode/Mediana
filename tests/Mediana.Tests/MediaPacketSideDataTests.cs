// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class MediaPacketSideDataTests
    {
        [Fact]
        public void PayloadAndSideDataShareOneBackingAllocationAcrossPacketRetains()
        {
            MediaBufferAllocator allocator = new(32);
            MediaPacket? retained = null;
            MediaPacket? timed = null;
            MediaPacketSideData? independent = null;
            try
            {
                using (WritableMediaBuffer writable = allocator.Allocate(32))
                {
                    for (int index = 0; index < 32; index++)
                    {
                        writable.Memory.Span[index] = (byte)index;
                    }
                    using MediaBuffer whole = writable.Publish();
                    using MediaBuffer payload = whole.Slice(9, 23);
                    using MediaBuffer header = whole.Slice(0, 9);
                    using MediaPacketSideData side = new("transport.protection", header);
                    using MediaPacket packet = new(0, payload, sideData: [side]);
                    Assert.Equal(1, packet.SideDataCount);
                    Assert.True(packet.TryGetSideData("transport.protection", out MediaPacketSideDataView view));
                    Assert.False(packet.TryGetSideData("other", out _));
                    Assert.True(MemoryMarshal.TryGetArray(packet.Data, out ArraySegment<byte> payloadArray));
                    Assert.True(MemoryMarshal.TryGetArray(view.Data, out ArraySegment<byte> sideArray));
                    Assert.Same(payloadArray.Array, sideArray.Array);
                    Assert.Equal(sideArray.Offset + 9, payloadArray.Offset);
                    retained = packet.Retain();
                    timed = packet.RetainWithDuration(new MediaDuration(1024, new TimeBase(1, 48000)));
                    independent = packet.RetainSideData(0);
                    Assert.Equal(32, allocator.AllocatedBytes);
                }
                Assert.Equal(32, allocator.AllocatedBytes);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, retained.GetSideData(0).Data.ToArray());
                Assert.Equal(retained.GetSideData(0), timed.GetSideData(0));
                retained.Dispose();
                timed.Dispose();
                Assert.Equal(32, allocator.AllocatedBytes);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, independent.Data.ToArray());
            }
            finally
            {
                retained?.Dispose();
                timed?.Dispose();
                independent?.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PartialConstructorFailureReleasesEveryAcquiredLease()
        {
            MediaBufferAllocator allocator = new(64);
            using (WritableMediaBuffer writable = allocator.Allocate(64))
            using (MediaBuffer buffer = writable.Publish())
            using (MediaPacketSideData first = new("first", buffer))
            using (MediaPacketSideData disposed = new("second", buffer))
            {
                disposed.Dispose();
                Assert.Throws<ObjectDisposedException>(() => new MediaPacket(0, buffer, sideData: [first, disposed]));
                Assert.Throws<ArgumentException>(() => new MediaPacket(0, buffer, sideData: [first, first]));
                Assert.Throws<ArgumentNullException>(() => new MediaPacket(0, buffer, sideData: [first, null!]));
                Assert.Equal(64, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void EmptyPacketsAndInvalidSideDataAccessPreservePayloadOwnership()
        {
            MediaBufferAllocator allocator = new(4);
            using (WritableMediaBuffer writable = allocator.Allocate(4))
            using (MediaBuffer buffer = writable.Publish())
            using (MediaPacket packet = new(0, buffer))
            using (MediaPacket empty = new(0, buffer, sideData: []))
            {
                Assert.Equal(0, packet.SideDataCount);
                Assert.Equal(0, empty.SideDataCount);
                Assert.False(packet.TryGetSideData("header", out _));
                Assert.Throws<ArgumentOutOfRangeException>(() => packet.GetSideData(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => packet.RetainSideData(-1));
                Assert.Throws<ArgumentException>(() => packet.TryGetSideData(" ", out _));
                Assert.Throws<ArgumentException>(() => new MediaPacketSideData("", buffer));
                using MediaPacket retained = packet.Retain();
                Assert.Equal(0, retained.SideDataCount);
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
