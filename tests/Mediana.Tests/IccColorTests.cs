// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class IccColorTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Icc");

        [Fact]
        public Task RgbGraySampledGammaAndAllParametricCurvesMatchLittleCms() => IccColorTestInput.VerifyAsync(Root);

        [Fact]
        public async Task SharedProfileConvertsConcurrentlyAndHonorsCancellationAndAllocationBudgets()
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "sampled-v2.icc"));
            byte[] pixels = [50, 100, 150, 51];
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame frame = IccColorTestInput.CreateFrame(profile, pixels, 1, 1, allocator))
            {
                Task<byte[]>[] tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    using VideoFrame converted = SrgbConverter.Apply(frame, allocator);
                    return converted.Data.ToArray();
                })).ToArray();
                byte[][] results = await Task.WhenAll(tasks);
                foreach (byte[] result in results) { Assert.Equal(results[0], result); }
                Assert.Equal(4, allocator.AllocatedBytes);
                Assert.ThrowsAny<OperationCanceledException>(() => SrgbConverter.Apply(frame, allocator, new(true)));
                MediaBufferAllocator small = new(3);
                Assert.Throws<MediaMemoryLimitException>(() => SrgbConverter.Apply(frame, small));
                Assert.Equal(0, small.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("header")]
        [InlineData("extent")]
        [InlineData("offset")]
        [InlineData("duplicate")]
        [InlineData("curve")]
        [InlineData("gamma")]
        public async Task MalformedProfilesAreRejectedWithoutRetainingOutput(string mutation)
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "gamma22-v2.icc"));
            switch (mutation)
            {
                case "header": profile[36] = 0; break;
                case "extent": BinaryPrimitives.WriteUInt32BigEndian(profile, uint.MaxValue); break;
                case "offset": BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(136), uint.MaxValue); break;
                case "duplicate": profile.AsSpan(132, 4).CopyTo(profile.AsSpan(144)); break;
                case "curve": BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(CurveOffset(profile) + 8), uint.MaxValue); break;
                case "gamma": profile.AsSpan(CurveOffset(profile) + 12, 2).Clear(); break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame frame = IccColorTestInput.CreateFrame(profile, [80, 60, 40, 127], 1, 1, allocator))
            {
                Assert.Throws<InvalidDataException>(() => SrgbConverter.Apply(frame, allocator));
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(8, "\u0005")]
        [InlineData(12, "link")]
        [InlineData(16, "CMYK")]
        [InlineData(20, "Lab ")]
        public async Task UnsupportedProfileModelsAreNotReinterpretedAsSrgb(int offset, string value)
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "linear-v2.icc"));
            System.Text.Encoding.ASCII.GetBytes(value).CopyTo(profile.AsSpan(offset));
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame frame = IccColorTestInput.CreateFrame(profile, [80, 60, 40, 127], 1, 1, allocator))
            {
                Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(frame, allocator));
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static int CurveOffset(ReadOnlySpan<byte> profile)
        {
            int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(profile[128..]));
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> entry = profile.Slice(132 + i * 12, 12);
                if (entry[..4].SequenceEqual("rTRC"u8)) { return checked((int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..])); }
            }
            throw new InvalidDataException("Test profile has no red tone curve.");
        }
    }
}
