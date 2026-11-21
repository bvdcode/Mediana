// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Alac.Tests
{
    public class AlacPacketTests
    {
        [Theory]
        [InlineData(16)]
        [InlineData(20)]
        [InlineData(24)]
        [InlineData(32)]
        public void RawPrecisionBackpressureAndRetainedOutput(int depth)
        {
            byte[] configuration = Configuration(depth);
            int[] expected = [int.MinValue, -1 << (32 - depth), 0, 1 << (32 - depth), int.MaxValue & (-1 << (32 - depth))];
            byte[] payload = RawPacket(expected, depth);
            MediaBufferAllocator allocator = new(4096);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AlacDecoderFactory().Create(Stream(configuration), allocator);
            using (WritableMediaBuffer writable = allocator.Allocate(payload.Length))
            {
                payload.CopyTo(writable.Memory.Span);
                using MediaBuffer buffer = writable.Publish();
                using MediaPacket packet = new(0, buffer, new(123, new(1, 48000)));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
            }
            using AudioFrame output = decoder.Receive().Value;
            Assert.Equal(expected, MemoryMarshal.Cast<byte, int>(output.GetPlane(0).Span).ToArray());
            decoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            decoder.Dispose();
            Assert.Equal(expected, MemoryMarshal.Cast<byte, int>(output.GetPlane(0).Span).ToArray());
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void TruncationCancellationAndResetReleaseEveryLease()
        {
            byte[] configuration = Configuration(24);
            byte[] payload = RawPacket([0, 256, -256, 65536, -65536], 24);
            MediaBufferAllocator packets = new(4096);
            MediaBufferAllocator allocator = new(4096);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AlacDecoderFactory().Create(Stream(configuration), allocator);
            for (int length = 1; length < payload.Length; length++)
            {
                using WritableMediaBuffer writable = packets.Allocate(length);
                payload.AsSpan(0, length).CopyTo(writable.Memory.Span);
                using MediaBuffer buffer = writable.Publish();
                using MediaPacket packet = new(0, buffer);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Equal(0, allocator.AllocatedBytes);
                decoder.Reset();
            }
            using WritableMediaBuffer full = packets.Allocate(payload.Length);
            payload.CopyTo(full.Memory.Span);
            using MediaBuffer published = full.Publish();
            using MediaPacket valid = new(0, published);
            Assert.Throws<OperationCanceledException>(() => decoder.TrySend(valid, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(valid);
            using AudioFrame frame = decoder.Receive().Value;
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ConfigurationPreservesDeclaredLayoutAndRejectsMismatchedCount()
        {
            byte[] configuration = new byte[48];
            Configuration(24).CopyTo(configuration, 0);
            BinaryPrimitives.WriteUInt32BigEndian(configuration.AsSpan(24), 24);
            "chan"u8.CopyTo(configuration.AsSpan(28));
            BinaryPrimitives.WriteUInt32BigEndian(configuration.AsSpan(36), (100u << 16) | 1);
            Assert.Equal(["front-center"], AlacConfiguration.Read(configuration).GetChannelOrder());
            configuration[39] = 2;
            Assert.Throws<InvalidDataException>(() => AlacConfiguration.Read(configuration));
        }

        private static EncodedStreamInfo Stream(byte[] configuration) => new(0, MediaKind.Audio,
            AlacConfiguration.CodecId, StreamFormats.Alac, new(1, 48000), configuration);

        private static byte[] Configuration(int depth)
        {
            byte[] data = new byte[24];
            BinaryPrimitives.WriteUInt32BigEndian(data, 16);
            data[5] = (byte)depth;
            data[6] = 40;
            data[7] = 10;
            data[8] = 14;
            data[9] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 48000);
            return data;
        }

        private static byte[] RawPacket(int[] samples, int depth)
        {
            List<bool> bits = [];
            void Write(uint value, int count)
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    bits.Add(((value >> i) & 1) != 0);
                }
            }
            Write(0, 19);
            Write(9, 4);
            Write((uint)samples.Length, 32);
            foreach (int sample in samples)
            {
                Write((uint)(sample >> (32 - depth)), depth);
            }
            Write(7, 3);
            byte[] result = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    result[i / 8] |= (byte)(1 << (7 - i % 8));
                }
            }
            return result;
        }
    }
}
