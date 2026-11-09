// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1RestorationFailureTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ScopedBudgetFailureReleasesRestorationUnitsBoundariesAndRows(int allocation)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "restorationMonoNoCdef.obu"));
            int samples = 120 * 88;
            int metadata = 30 * 22 * Unsafe.SizeOf<Av1FilterBlock>();
            int units = Unsafe.SizeOf<Av1RestorationUnit>();
            int contexts = (30 + Av1TileDecoder<byte>.LeftContexts) * Unsafe.SizeOf<Av1BlockContext<byte>>();
            int boundary = 120 * 4;
            int scratch = 120 * 10;
            int reserved = allocation switch
            {
                0 => samples + metadata,
                1 => samples + metadata + units + contexts + samples / 16,
                2 => samples + metadata + units + contexts + samples / 16 + boundary,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            int requested = allocation switch
            {
                0 => units,
                1 => boundary,
                2 => scratch,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator parent = new(1024 * 1024);
            MediaBufferAllocator allocator = parent.CreateScope(reserved + requested - 1);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), new byte[] { 0x81, 0, 0x1C, 0 });
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            {
                using WritableMediaBuffer write = input.Allocate(encoded.Length);
                encoded.CopyTo(write.Span);
                using MediaBuffer buffer = write.Publish();
                using MediaPacket packet = new(0, buffer, new(0, new(1, 25)));
                MediaMemoryLimitException error = Assert.Throws<MediaMemoryLimitException>(() => codec.TrySend(packet));
                Assert.Equal(requested, error.RequestedBytes);
                Assert.Equal(0, parent.AllocatedBytes);
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                byte[] small = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "flat.obu"));
                using WritableMediaBuffer smallWrite = input.Allocate(small.Length);
                small.CopyTo(smallWrite.Span);
                using MediaBuffer smallBuffer = smallWrite.Publish();
                using MediaPacket smallPacket = new(0, smallBuffer, new(0, new(1, 25)));
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(smallPacket));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, input.AllocatedBytes);
            Assert.Equal(0, parent.AllocatedBytes);
        }
    }
}
