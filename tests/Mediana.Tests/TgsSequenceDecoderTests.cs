// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Images.Tgs;
using Mediana.IO;

namespace Mediana.Tests
{
    public class TgsSequenceDecoderTests
    {
        [Fact]
        public async Task CommonSequencePreservesInPointPartialDurationAndEarlyDisposal()
        {
            byte[] encoded = "{\"w\":2,\"h\":1,\"fr\":4,\"ip\":10,\"op\":11.5,\"layers\":[]}"u8.ToArray();
            MediaBufferAllocator allocator = new(1024);
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new TgsImageDecoder()).Build();
            await using RandomAccessStreamByteSource source = new(new MemoryStream(encoded, writable: false));
            int index = 0;
            await foreach (VideoFrame frame in engine.DecodeImageFramesAsync(source, allocator))
            {
                using (frame)
                {
                    Assert.Equal(new MediaTimestamp(index, new(1, 4)), frame.Timestamp);
                    Assert.Equal(new MediaDuration(index == 0 ? 2500000 : 1250000, TimeBase.TimeSpanTicks), frame.Duration);
                    Assert.Equal(8, frame.Data.Length);
                    index++;
                }
            }
            Assert.Equal(2, index);
            Assert.Equal(0, allocator.AllocatedBytes);
            await foreach (VideoFrame frame in engine.DecodeImageFramesAsync(encoded, allocator, new() { Time = new(1, new(1, 4)) }))
            {
                using (frame) { Assert.Equal(new MediaTimestamp(1, new(1, 4)), frame.Timestamp); }
                break;
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await foreach (VideoFrame frame in engine.DecodeImageFramesAsync(encoded, allocator, new() { MaximumPixels = 1 })) { frame.Dispose(); }
            });
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
