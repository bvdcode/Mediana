// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3TransientAccessUnitTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(6)]
        public void FutureTransientReconstructionIsIndependentOfSyncframeAccessUnitGrouping(int groupSize)
        {
            byte[][] frames = Enumerable.Range(0, 40).Select(index => Ac3SyntheticFrame.Create(true, false, 1,
                transientLocation: index < 16 ? 4092 : null, transientLength: 255)).ToArray();
            byte[] expected = Decode(frames, 1);
            Assert.Equal(40 * 256 * sizeof(float), expected.Length);
            Assert.Equal(expected, Decode(frames, groupSize));
        }

        private static byte[] Decode(byte[][] frames, int groupSize)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream result = new();
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            {
                for (int index = 0; index < frames.Length; index += groupSize)
                {
                    byte[] bytes = frames.Skip(index).Take(groupSize).SelectMany(frame => frame).ToArray();
                    using MediaPacket packet = Ac3DecoderTests.Packet(bytes, allocator, index * 256);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    Drain();
                }
                decoder.CompleteInput(); Drain();
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);

                void Drain()
                {
                    while (decoder.Receive() is { Status: CodecOutputStatus.Available } output)
                    {
                        using AudioFrame frame = output.Value;
                        Assert.Equal(new MediaTimestamp(result.Length / sizeof(float), new(1, 48000)), frame.Timestamp);
                        result.Write(frame.GetPlane(0).Span);
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            return result.ToArray();
        }
    }
}
