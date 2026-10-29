// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Containers.Mp3;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp3MetadataTests
    {
        [Fact]
        public async Task TextVersionRulesGroupPrefixesAndDataLengthIndicatorsAreDistinct()
        {
            byte[] title = [3, .. "first\0second"u8.ToArray()];
            byte[] body = [.. Mp3TestInput.MetadataFrame("TIT2", [7, .. Mp3TestInput.Synchsafe(title.Length), .. title], flags: 0x41),
                .. Mp3TestInput.MetadataFrame("TXXX", [1, 0xff, 0xfe, .. Encoding.Unicode.GetBytes("kind\0value")])];
            await using ShortReadSource source = new([.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(new[] { "first", "second" }, reader.TextFrames[0].Values);
                Assert.Equal("kind", reader.TextFrames[1].Description);
                Assert.Equal("value", Assert.Single(reader.TextFrames[1].Values));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task VersionThreeIgnoresTextAfterTheFirstTerminator()
        {
            byte[] frame = Mp3TestInput.MetadataFrame("TIT2", [0, .. "title\0ignored"u8.ToArray()], 3);
            await using ShortReadSource source = new([.. Mp3TestInput.Tag(3, frame), .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            Assert.Equal("title", Assert.Single(Assert.Single(reader.TextFrames).Values));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task PictureDescriptionEncodingsPreserveOriginalImage(int encoding)
        {
            byte[] body = Mp3TestInput.MetadataFrame("APIC", Mp3TestInput.Picture([1, 2, 3], "cover", encoding));
            await using ShortReadSource source = new([.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            Assert.Equal("cover", reader.Pictures[0].Description);
            using MediaBuffer image = reader.ReadPicture(0);
            Assert.Equal(new byte[] { 1, 2, 3 }, image.Memory.ToArray());
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public async Task PlainExtendedHeadersAndPaddingAreSupported(int version)
        {
            byte[] extension = version == 3 ? [0, 0, 0, 6, 0, 0, 0, 0, 0, 7] : [0, 0, 0, 6, 1, 0];
            byte[] body = [.. extension, .. Mp3TestInput.MetadataFrame("TIT2", [0, 65], version), .. new byte[7]];
            await using ShortReadSource source = new([.. Mp3TestInput.Tag(version, body, 0x40), .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            Assert.Equal("A", reader.TextFrames[0].Values[0]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        public async Task MalformedTagsReleaseAllAllocatedStorage(int kind)
        {
            byte[] title = Mp3TestInput.MetadataFrame("TIT2", [3, 65]);
            byte[] file = kind switch
            {
                0 => Mp3TestInput.Tag(4, title, 1),
                1 => Mp3TestInput.Tag(4, [.. title, .. title]),
                2 => Mp3TestInput.Tag(4, [.. title, 0, 1]),
                3 => Mp3TestInput.Tag(4, title[..^1]),
                4 => Mp3TestInput.Tag(4, Mp3TestInput.MetadataFrame("APIC", [0, 0, 3, 65])),
                5 => Mp3TestInput.Tag(4, Mp3TestInput.MetadataFrame("TIT2", [1, 65, 0])),
                6 => Mp3TestInput.Tag(3, Mp3TestInput.MetadataFrame("TIT2", [3, 65], 3)),
                7 => Mp3TestInput.Tag(4, title, 0x80),
                8 => Mp3TestInput.Tag(4, [.. title, 0], 0x10),
                9 => Mp3TestInput.Tag(4, Mp3TestInput.MetadataFrame("TIT2", [0, 0, 0, 7, 3, 65], flags: 1)),
                10 => Mp3TestInput.Tag(4, Mp3TestInput.MetadataFrame("TIT2", [3, 65], flags: 0x80)),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            await using ShortReadSource source = new([.. file, .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await Assert.ThrowsAsync<InvalidDataException>(() => Mp3Reader.OpenAsync(source, allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task LimitsAndUnsupportedFramesAreExplicit(int kind)
        {
            byte[] title = Mp3TestInput.MetadataFrame("TIT2", [3, 65, 66]);
            byte[] body = kind switch
            {
                2 => [.. title, .. Mp3TestInput.MetadataFrame("TPE1", [3, 65])],
                3 => Mp3TestInput.MetadataFrame("TIT2", [3, 65], flags: 8),
                _ => title
            };
            Mp3ReadOptions options = kind switch
            {
                0 => new() { MaxMetadataBytes = 10 },
                1 => new() { MaxTextBytes = 1 },
                2 => new() { MaxMetadataFrames = 1 },
                3 => new(),
                4 => new() { MaxFrameBytes = 100 },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            await using ShortReadSource source = new([.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame()]);
            MediaBufferAllocator allocator = new(4096);
            await Assert.ThrowsAsync<NotSupportedException>(() => Mp3Reader.OpenAsync(source, allocator, options).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
