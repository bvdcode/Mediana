// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Codecs.Dts.Tests
{
    public class DtsPacketTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task AllWordPackingsPreserveSamples(bool fourteenBit, bool littleEndian)
        {
            MediaBufferAllocator packets = new(1024 * 1024), allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "stereo.mkv")));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, packets);
            using MediaPacket original = await reader.ReadPacketAtAsync(0, 0);
            byte[] packed = Pack(original.Data.Span, fourteenBit, littleEndian);
            DtsFrameHeader header = DtsFrameHeader.Read(packed);
            Assert.Equal(48000, header.SampleRate);
            Assert.Equal(packed.Length, header.FrameBytes);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new DtsDecoderFactory().Create(reader.Streams[0], allocator);
            decoder.TrySend(original);
            using AudioFrame reference = decoder.Receive().Value;
            decoder.Reset();
            using MediaPacket packet = Packet(packed, packets);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
            using AudioFrame output = decoder.Receive().Value;
            Assert.Equal(reference.GetPlane(0).ToArray(), output.GetPlane(0).ToArray());
            decoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            decoder.Dispose();
            Assert.Equal(reference.GetPlane(0).ToArray(), output.GetPlane(0).ToArray());
            reference.Dispose(); output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task TruncationCancellationAndResetReleaseState()
        {
            MediaBufferAllocator packets = new(1024 * 1024), allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "stereo.mkv")));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, packets);
            using MediaPacket original = await reader.ReadPacketAtAsync(0, 0);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new DtsDecoderFactory().Create(reader.Streams[0], allocator);
            foreach (int length in new[] { 1, 3, 10, 18, 32, original.Data.Length - 1 })
            {
                using MediaPacket packet = Packet(original.Data.Span[..length].ToArray(), packets);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Equal(0, allocator.AllocatedBytes);
                decoder.Reset();
            }
            Assert.Throws<OperationCanceledException>(() => decoder.TrySend(original, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(original);
            Assert.Throws<OperationCanceledException>(() => decoder.Receive(new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(original);
            using AudioFrame frame = decoder.Receive().Value;
            decoder.Dispose(); frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static byte[] Pack(ReadOnlySpan<byte> original, bool fourteenBit, bool littleEndian)
        {
            int wordBits = fourteenBit ? 14 : 16;
            byte[] packed = new byte[(original.Length * 8 + wordBits - 1) / wordBits * 2];
            for (int word = 0; word < packed.Length / 2; word++)
            {
                int value = 0;
                for (int i = 0; i < wordBits; i++)
                {
                    int position = word * wordBits + i;
                    int bit = position < original.Length * 8 ? (original[position / 8] >> (7 - position % 8)) & 1 : 0;
                    value = value << 1 | bit;
                }
                if (fourteenBit && word == 1) { value |= 0xc000; }
                if (littleEndian) { BinaryPrimitives.WriteUInt16LittleEndian(packed.AsSpan(word * 2), (ushort)value); }
                else { BinaryPrimitives.WriteUInt16BigEndian(packed.AsSpan(word * 2), (ushort)value); }
            }
            return packed;
        }

        [Fact]
        public async Task TerminationAddsDeclaredPaddingWithoutDiscardingCodedSamples()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "stereo.mkv")));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            using MediaPacket original = await reader.ReadPacketAtAsync(0, 0);
            byte[] data = original.Data.ToArray();
            data[4] = (byte)((data[4] & 3) | 6 << 2);
            DtsFrameHeader header = DtsFrameHeader.Read(data);
            Assert.True(header.IsTermination);
            Assert.Equal(7, header.DeficitSamples);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new DtsDecoderFactory().Create(reader.Streams[0], allocator);
            decoder.TrySend(original);
            using AudioFrame reference = decoder.Receive().Value;
            decoder.Reset();
            using MediaPacket packet = Packet(data, allocator);
            decoder.TrySend(packet);
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(reference.SamplesPerChannel + 7, frame.SamplesPerChannel);
            Assert.Equal(reference.GetPlane(0).ToArray(), frame.GetPlane(0).Span[..reference.GetPlane(0).Length].ToArray());
            Assert.All(frame.GetPlane(0).Span[reference.GetPlane(0).Length..].ToArray(), value => Assert.Equal(0, value));
        }

        [Theory]
        [InlineData("mono44100", 98)]
        [InlineData("stereo", 99)]
        public async Task SumDifferenceRequiresAnExistingSpeakerPair(string name, int bit)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".mkv")));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            using MediaPacket original = await reader.ReadPacketAtAsync(0, 0);
            byte[] data = original.Data.ToArray();
            Assert.False(DtsFrameHeader.Read(data).HasCrc);
            data[bit / 8] |= (byte)(1 << (7 - bit % 8));
            Assert.Throws<InvalidDataException>(() => DtsFrameHeader.Read(data));
        }

        private static MediaPacket Packet(byte[] data, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer);
        }
    }
}
