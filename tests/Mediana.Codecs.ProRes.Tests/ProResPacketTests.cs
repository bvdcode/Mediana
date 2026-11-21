// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.ProRes.Tests
{
    public class ProResPacketTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        public void NormativeAlphaIsLosslessAndIndependentFromColorPrecision(int depth)
        {
            int maximum = (1 << depth) - 1;
            int[] samples = [0, 1, maximum, maximum / 2, 2, 33, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16];
            byte[] payload = CreatePicture(depth, samples);
            MediaBufferAllocator allocator = new(65536), packetAllocator = new(65536);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new ProResDecoderFactory().Create(Stream(), allocator);
            using MediaPacket packet = Packet(payload, packetAllocator);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
            using VideoFrame frame = decoder.Receive().Value;
            Assert.Equal(samples.Select(value => (ushort)(depth == 8 ? value * 257 : value)).ToArray(),
                MemoryMarshal.Cast<byte, ushort>(frame.GetRow(3, 0)).ToArray());
            decoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            decoder.Dispose();
            Assert.Equal(samples[0], MemoryMarshal.Cast<byte, ushort>(frame.GetRow(3, 0))[0]);
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void TruncationCancellationAndResetReleaseEveryLease()
        {
            byte[] payload = CreatePicture(16, Enumerable.Range(0, 16).ToArray());
            MediaBufferAllocator allocator = new(65536), packetAllocator = new(65536);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new ProResDecoderFactory().Create(Stream(), allocator);
            for (int length = 1; length < payload.Length; length++)
            {
                byte[] truncated = payload[..length];
                if (length >= 4) { BinaryPrimitives.WriteUInt32BigEndian(truncated, (uint)length); }
                using MediaPacket packet = Packet(truncated, packetAllocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Equal(0, allocator.AllocatedBytes);
                decoder.Reset();
            }
            using MediaPacket valid = Packet(payload, packetAllocator);
            Assert.Throws<OperationCanceledException>(() => decoder.TrySend(valid, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(valid);
            using VideoFrame frame = decoder.Receive().Value;
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static EncodedStreamInfo Stream() => new(0, MediaKind.Video, "prores", StreamFormats.ProRes, new(1, 25), []);

        private static MediaPacket Packet(byte[] payload, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(payload.Length);
            payload.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer, new(10, new(1, 25)), duration: new(1, new(1, 25)));
        }

        private static byte[] CreatePicture(int alphaDepth, int[] samples)
        {
            List<bool> bits = [];
            void Write(int value, int count)
            {
                for (int shift = count - 1; shift >= 0; shift--) { bits.Add(((value >> shift) & 1) != 0); }
            }
            int previous = (1 << alphaDepth) - 1;
            foreach (int sample in samples)
            {
                Write(1, 1);
                Write((sample - previous) & ((1 << alphaDepth) - 1), alphaDepth);
                Write(1, 1);
                previous = sample;
            }
            int alphaBytes = (bits.Count + 7) / 8;
            byte[] encoded = new byte[28 + 8 + 2 + 8 + 9 + alphaBytes];
            BinaryPrimitives.WriteUInt32BigEndian(encoded, (uint)encoded.Length);
            "icpf"u8.CopyTo(encoded.AsSpan(4));
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(8), 20);
            encoded[11] = 1;
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(16), 16);
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(18), 1);
            encoded[20] = 3 << 6;
            encoded[21] = 1 << 4;
            encoded[22] = encoded[23] = encoded[24] = 1;
            encoded[25] = (byte)(alphaDepth == 8 ? 1 : 2);
            encoded[28] = 8 << 3;
            BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(29), (uint)(encoded.Length - 28));
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(33), 1);
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(36), (ushort)(encoded.Length - 38));
            encoded[38] = 8 << 3;
            encoded[39] = 1;
            for (int plane = 0; plane < 3; plane++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(40 + plane * 2), 3);
                // First DC uses Exp-Golomb(5), then differences use orders 3, 0, 0.
                encoded[46 + plane * 3] = 0b10000010;
                encoded[47 + plane * 3] = 0b00110000;
            }
            for (int index = 0; index < bits.Count; index++)
            {
                if (bits[index]) { encoded[55 + index / 8] |= (byte)(1 << (7 - index % 8)); }
            }
            return encoded;
        }
    }
}
