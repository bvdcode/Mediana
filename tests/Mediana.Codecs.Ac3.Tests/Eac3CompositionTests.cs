// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Eac3CompositionTests
    {
        [Theory]
        [InlineData(false, 8)]
        [InlineData(true, 16)]
        public void ReproducesIndependentProgrammeReplacementAndExtensionFixtures(bool wide, int channels)
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data($"dependent-{channels}.eac3")), Ac3DependentFrame.CreateSequence(wide));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LastDependentDynamicRangeAppliesToTheCompleteProgramme(bool wide)
        {
            byte[] data = Ac3DependentFrame.CreateSequence(wide, true);
            float[] fullRange = Decode(data, false), compressed = Decode(data, true);
            Assert.Equal(fullRange.Length, compressed.Length);
            Assert.Contains(fullRange, value => Math.Abs(value) > .0001f);
            for (int sample = 0; sample < fullRange.Length; sample++) { Assert.Equal(fullRange[sample] * 2, compressed[sample]); }
        }

        [Fact]
        public void OrphanUnorderedAndUnsynchronizedDependentFramesFailBeforeOutput()
        {
            byte[] main = Ac3DependentFrame.Create(0), dependent = Ac3DependentFrame.Create(1);
            byte[] order = Ac3TestBitWriter.WithBit(dependent, 18, true);
            byte[] timing = Ac3TestBitWriter.WithBit(dependent, 34, false);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            {
                foreach (byte[] data in new byte[][] { dependent, [.. main, .. order], [.. main, .. timing] })
                {
                    using MediaPacket packet = Ac3DecoderTests.Packet(data, allocator);
                    Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                    Assert.Throws<InvalidOperationException>(() => decoder.Receive()); decoder.Reset();
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void EightOrderedDependentSubstreamsReplaceChannelsUsingTheFinalOwner()
        {
            List<byte> data = [];
            data.AddRange(Ac3DependentFrame.Create(0));
            for (int component = 0; component < 8; component++)
            {
                byte[] frame = Ac3DependentFrame.Create(1, wide: component < 7);
                for (int bit = 0; bit < 3; bit++) { frame = Ac3TestBitWriter.WithBit(frame, 18 + bit, (component & 1 << (2 - bit)) != 0); }
                data.AddRange(frame);
            }
            float[] actual = Decode(data.ToArray(), false);
            float[] expected = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(Ac3DecoderTests.Data("dependent-8.pcm"))).ToArray();
            Assert.Equal(768 * 8, actual.Length);
            for (int index = 0; index < actual.Length; index++) { Assert.InRange(Math.Abs(actual[index] - expected[index]), 0, 1e-8f); }
        }

        [Fact]
        public void MissingCustomMapReplacesBaseLocationsWithoutInventingExtraChannels()
        {
            byte[] data = [.. Ac3DependentFrame.Create(0), .. Ac3DependentFrame.Create(1, customMap: false)];
            float[] actual = Decode(data, false);
            float[] reference = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(Ac3DecoderTests.Data("dependent-8.pcm"))).ToArray();
            int[] locations = [3, 1, 4, 6, 7, 5];
            Assert.Equal(768 * 6, actual.Length);
            for (int sample = 0; sample < 768; sample++)
            {
                for (int channel = 0; channel < 6; channel++)
                { Assert.InRange(Math.Abs(actual[sample * 6 + channel] - reference[sample * 8 + locations[channel]]), 0, 1e-8f); }
            }
        }

        [Fact]
        public void CompositionFailureReleasesAllReservedSubstreamWorkspaces()
        {
            byte[] data = [.. Ac3DependentFrame.Create(0), .. Ac3DependentFrame.Create(1)];
            MediaBufferAllocator inputAllocator = new(1024 * 1024), allocator = new(60000);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            using (MediaPacket packet = Ac3DecoderTests.Packet(data, inputAllocator))
            {
                Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.Equal(0, inputAllocator.AllocatedBytes);
        }

        private static float[] Decode(byte[] data, bool dynamicRange)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream result = new();
            Eac3DecoderFactory factory = new(dynamicRange);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "eac3", Mediana.Formats.StreamFormats.Eac3, new(1, 48000));
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(stream, allocator))
            {
                long position = 0;
                for (int offset = 0; offset < data.Length;)
                {
                    Ac3FrameHeader header = Ac3FrameHeader.Read(data.AsSpan(offset));
                    int length = header.FrameBytes;
                    while (offset + length < data.Length && Ac3FrameHeader.Read(data.AsSpan(offset + length)).StreamType == 1)
                    { length += Ac3FrameHeader.Read(data.AsSpan(offset + length)).FrameBytes; }
                    using MediaPacket packet = Ac3DecoderTests.Packet(data.AsSpan(offset, length), allocator, position);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet)); offset += length; position += header.SamplesPerFrame;
                    Drain();
                }
                decoder.CompleteInput(); Drain();
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);

                void Drain()
                {
                    while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                    { using AudioFrame frame = available.Value; result.Write(frame.GetPlane(0).Span); }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            return MemoryMarshal.Cast<byte, float>(result.ToArray()).ToArray();
        }
    }
}
