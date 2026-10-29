// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp3BoundaryTests
    {
        [Fact]
        public async Task TruncatedFirstFrameAndInsufficientBudgetReleaseStorage()
        {
            byte[] frame = Mp3TestInput.Frame();
            MediaBufferAllocator allocator = new(4096);
            await using ShortReadSource truncated = new(frame[..^1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => Mp3Reader.OpenAsync(truncated, allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(128 + frame.Length - 1);
            await using ShortReadSource source = new(frame);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => Mp3Reader.OpenAsync(source, small).AsTask());
            Assert.Equal(0, small.AllocatedBytes);
        }

        [Fact]
        public async Task SampleRateChangeIsExplicitAndReleased()
        {
            await using ShortReadSource source = new([.. Mp3TestInput.Frame(), .. Mp3TestInput.Frame("FFFB9464")]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            using (MediaPacket? packet = await reader.ReadPacketAsync())
            {
                Assert.NotNull(packet);
            }
            await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FrameLimitAcceptsExactEndButRejectsAdditionalFrame()
        {
            byte[] frame = Mp3TestInput.Frame();
            MediaBufferAllocator allocator = new(4096);
            await using ShortReadSource source = new([.. frame, .. frame]);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator, new Mp3ReadOptions { MaxAudioFrames = 1 });
            using (MediaPacket? packet = await reader.ReadPacketAsync())
            {
                Assert.NotNull(packet);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            await using ShortReadSource exact = new(frame);
            await using (Mp3Reader complete = await Mp3Reader.OpenAsync(exact, allocator, new Mp3ReadOptions { MaxAudioFrames = 1 }))
            {
                using MediaPacket? packet = await complete.ReadPacketAsync();
                Assert.Null(await complete.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FooterMustMatchAndPicturesMustHaveDistinctDescriptions()
        {
            byte[] body = Mp3TestInput.MetadataFrame("APIC", Mp3TestInput.Picture([1]));
            byte[] tag = Mp3TestInput.Tag(4, body, 0x10);
            tag[^1] ^= 1;
            MediaBufferAllocator allocator = new(4096);
            await using ShortReadSource invalid = new([.. tag, .. Mp3TestInput.Frame()]);
            await Assert.ThrowsAsync<InvalidDataException>(() => Mp3Reader.OpenAsync(invalid, allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            await using ShortReadSource duplicates = new([.. Mp3TestInput.Tag(4, [.. body, .. body]), .. Mp3TestInput.Frame()]);
            await Assert.ThrowsAsync<InvalidDataException>(() => Mp3Reader.OpenAsync(duplicates, allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FrameUnsynchronisationPreservesFollowingHeadersAndDeclaredPayloadLength()
        {
            byte[] picture = Mp3TestInput.Picture([0xff, 0xe0, 0xff, 0, 0xff]);
            byte[] payload = [7, .. Mp3TestInput.Synchsafe(picture.Length), .. picture];
            byte[] body = [.. Mp3TestInput.MetadataFrame("APIC", Mp3TestInput.Unsynchronize(payload), flags: 0x43),
                .. Mp3TestInput.MetadataFrame("TIT2", [3, 65])];
            await using ShortReadSource source = new([.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                using MediaBuffer pictureBytes = reader.ReadPicture(0);
                Assert.Equal(new byte[] { 0xff, 0xe0, 0xff, 0, 0xff }, pictureBytes.Memory.ToArray());
                Assert.Equal("A", reader.TextFrames[0].Values[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ProbeRequiresLayerThreeOrAnId3Signature()
        {
            Mp3ReaderFactory factory = new();
            Assert.True(factory.CanRead("ID3\u0004"u8));
            Assert.True(factory.CanRead(Convert.FromHexString("FFF394C0")));
            Assert.False(factory.CanRead(Convert.FromHexString("FFFD9064")));
            Assert.False(factory.CanRead("fLaC"u8));
            Assert.False(factory.CanRead([0xff, 0xfb]));
        }
    }
}
