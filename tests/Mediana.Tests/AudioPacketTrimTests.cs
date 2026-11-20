// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AudioPacketTrimTests
    {
        [Fact]
        public void TrimSideDataRetainsItsOwnStorageAndValidatesCounts()
        {
            MediaBufferAllocator allocator = new(1024);
            using MediaPacketSideData side = new AudioPacketTrim(1057, 301).CreateSideData(allocator);
            using MediaPacketSideData retained = side.Retain();
            side.Dispose();
            Assert.Equal(new AudioPacketTrim(1057, 301), AudioPacketTrim.Read(new(retained.Kind, retained.Data)));
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPacketTrim(-1, 0).CreateSideData(allocator));
            Assert.Throws<InvalidDataException>(() => AudioPacketTrim.Read(new(AudioPacketTrim.Kind, new byte[7])));
            Assert.Throws<InvalidDataException>(() => AudioPacketTrim.Read(new(AudioPacketTrim.Kind, new byte[] { 255, 255, 255, 255, 0, 0, 0, 0 })));
        }
    }
}
