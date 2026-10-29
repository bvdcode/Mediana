// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264IntraDecoderTests
    {
        [Theory]
        [InlineData("avc-tail", ".mov", 0, "8df41101ddfc3feb7ad3b4f8f7ac3d94815c0745b0dac7ca4d2d2c174685958c")]
        [InlineData("avc-tail", ".mov", 5, "dcded925869f945a85a604c356348a2f194f0d9ed5b7808b73222ee7307dca3d")]
        [InlineData("avc-cropped", ".mp4", 0, "fe331966ff4a7fb750238e45a5bd01670d3c284dd273208e3312296a48acd472")]
        public async Task BaselineIdrMatchesIndependentFilteredYuvSampleForSample(string name, string extension, int packetIndex, string hash)
        {
            (H264Configuration configuration, byte[] nal) = await ReadIdrAsync(name + extension, packetIndex);
            byte[] expected = await Mp4TestData.ReadAsync($"{name}.frame{packetIndex}.yuv");
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(expected)));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture actual = H264IntraDecoder.Decode(nal, configuration, allocator))
            {
                Assert.Equal(80, actual.Width);
                Assert.Equal(48, actual.Height);
                H264TestInput.VerifyPicture(actual, expected);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task DispatchesCabacAndReleasesStorageAfterTruncatedSlices()
        {
            (H264Configuration high, byte[] cabac) = await ReadIdrAsync("avc-aac.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = H264IntraDecoder.Decode(cabac, high, allocator))
            {
                H264TestInput.VerifyPicture(picture, await Mp4TestData.ReadAsync("avc-aac.frame0.yuv"));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            (H264Configuration baseline, byte[] nal) = await ReadIdrAsync("avc-tail.mov");
            foreach (int length in new[] { 0, 1, 2, 3, nal.Length / 2, nal.Length - 1 })
            {
                Assert.Throws<InvalidDataException>(() => H264IntraDecoder.Decode(nal.AsSpan(0, length), baseline, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task ChecksCancellationAndPictureBudget()
        {
            (H264Configuration configuration, byte[] nal) = await ReadIdrAsync("avc-tail.mov");
            MediaBufferAllocator allocator = new(32);
            Assert.Throws<MediaMemoryLimitException>(() => H264IntraDecoder.Decode(nal, configuration, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<OperationCanceledException>(() => H264IntraDecoder.Decode(nal, configuration, allocator, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MutatedSlicesRemainBoundedAndReleaseTheirPictureStorage()
        {
            (H264Configuration configuration, byte[] nal) = await ReadIdrAsync("avc-tail.mov");
            MediaBufferAllocator allocator = new(1024 * 1024);
            Random random = new(264);
            for (int iteration = 0; iteration < 128; iteration++)
            {
                byte[] mutated = (byte[])nal.Clone();
                mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
                Exception? error = Record.Exception(() =>
                {
                    using H264Picture picture = H264IntraDecoder.Decode(mutated, configuration, allocator);
                });
                Assert.True(error is null or InvalidDataException or NotSupportedException, error?.ToString());
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static Task<(H264Configuration Configuration, byte[] Nal)> ReadIdrAsync(string name, int packetIndex = 0) =>
            H264TestInput.ReadIdrAsync(Mp4TestData.PathFor(name), packetIndex);
    }
}
