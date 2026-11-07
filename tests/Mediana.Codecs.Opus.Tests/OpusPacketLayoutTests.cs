// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusPacketLayoutTests
    {
        [Fact]
        public void FramesAndConsumedStreamBoundariesMatchIndependentReference()
        {
            using BinaryReader input = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "packet-layout.bin")));
            Assert.Equal("OPUSPKT1"u8.ToArray(), input.ReadBytes(8));
            int cases = input.ReadInt32(), valid = 0;
            Range[] frames = new Range[48];
            Range unchanged = new(17, 29);
            for (int test = 0; test < cases; test++)
            {
                byte[] packet = input.ReadBytes(input.ReadInt32());
                bool selfDelimited = input.ReadBoolean();
                int count = input.ReadInt32();
                frames.AsSpan().Fill(unchanged);
                if (count < 0)
                {
                    Assert.Throws<InvalidDataException>(() => Read(packet, selfDelimited, frames, out _));
                    Assert.All(frames, frame => Assert.Equal(unchanged, frame));
                    continue;
                }
                int expectedConsumed = input.ReadInt32(), samples = input.ReadInt32();
                OpusPacketInfo result = Read(packet, selfDelimited, frames, out int consumed);
                Assert.Equal(count, result.FrameCount);
                Assert.Equal(samples, result.SamplesPerFrame);
                Assert.Equal(expectedConsumed, consumed);
                for (int frame = 0; frame < count; frame++)
                {
                    Assert.Equal(input.ReadInt32(), frames[frame].Start.Value);
                    Assert.Equal(input.ReadInt32(), frames[frame].End.Value - frames[frame].Start.Value);
                    Assert.InRange(frames[frame].End.Value, frames[frame].Start.Value, consumed);
                }
                for (int frame = count; frame < frames.Length; frame++) { Assert.Equal(unchanged, frames[frame]); }
                valid++;
            }
            Assert.Equal(4004, cases);
            Assert.Equal(2372, valid);
            Assert.Equal(input.BaseStream.Length, input.BaseStream.Position);
        }

        [Fact]
        public void ConsecutiveSelfDelimitedStreamsExcludeEachOthersPayloadsAndPadding()
        {
            byte[] packet = [0x83, 0xc2, 2, 1, 2, 11, 23, 37, 0, 0, 0x80, 1, 41, 0x80, 53];
            Span<Range> frames = stackalloc Range[48];
            OpusPacketInfo first = OpusPacketInfo.ReadSelfDelimitedFrames(packet, frames, out int consumed);
            Assert.Equal(2, first.FrameCount);
            Assert.Equal(10, consumed);
            Assert.Equal(new byte[] { 11 }, packet[frames[0]]);
            Assert.Equal(new byte[] { 23, 37 }, packet[frames[1]]);
            OpusPacketInfo second = OpusPacketInfo.ReadSelfDelimitedFrames(packet.AsSpan(consumed), frames, out int secondBytes);
            Assert.Equal(1, second.FrameCount);
            Assert.Equal(3, secondBytes);
            Assert.Equal(new byte[] { 41 }, packet.AsSpan(consumed)[frames[0]].ToArray());
            OpusPacketInfo third = OpusPacketInfo.ReadFrames(packet.AsSpan(consumed + secondBytes), frames);
            Assert.Equal(1, third.FrameCount);
            Assert.Equal(new byte[] { 53 }, packet.AsSpan(consumed + secondBytes)[frames[0]].ToArray());
        }

        [Fact]
        public void FrameRangesRequireNoHeapAllocationsAndAShortDestinationIsUnchanged()
        {
            byte[] packet = [0x83, 0x83, 1, 2, 11, 23, 37, 41, 53, 67];
            Span<Range> frames = stackalloc Range[48];
            OpusPacketInfo.ReadFrames(packet, frames);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int total = 0;
            for (int index = 0; index < 10000; index++) { total += OpusPacketInfo.ReadFrames(packet, frames).FrameCount; }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(30000, total);
            Assert.Equal(0, allocated);
            Range[] shortDestination = [new(17, 29)];
            Assert.Throws<ArgumentException>(() => OpusPacketInfo.ReadFrames(packet, shortDestination));
            Assert.Equal(new Range(17, 29), shortDestination[0]);
        }

        private static OpusPacketInfo Read(byte[] packet, bool selfDelimited, Range[] frames, out int consumed)
        {
            if (selfDelimited) { return OpusPacketInfo.ReadSelfDelimitedFrames(packet, frames, out consumed); }
            consumed = packet.Length;
            return OpusPacketInfo.ReadFrames(packet, frames);
        }
    }
}
