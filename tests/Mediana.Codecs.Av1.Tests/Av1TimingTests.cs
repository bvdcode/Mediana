// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TimingTests
    {
        [Theory]
        [InlineData("timingEqual")]
        [InlineData("timingVariable")]
        [InlineData("timingModel")]
        public void IndependentSequencesPreservePixelsPacketTimesAndReset(string fixture) =>
            Av1TimingTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), fixture);

        [Theory]
        [InlineData("timingEqual", true, false)]
        [InlineData("timingVariable", false, false)]
        [InlineData("timingModel", false, true)]
        public void ReadsIndependentClockAndModelSyntax(string fixture, bool equalInterval, bool model)
        {
            Av1SequenceHeader header = Sequence(fixture);
            Av1SequenceTiming timing = Assert.IsType<Av1SequenceTiming>(header.Timing);
            Assert.Equal(1001U, timing.Info.NumUnitsInDisplayTick);
            Assert.Equal(30000U, timing.Info.TimeScale);
            Assert.Equal(equalInterval, timing.Info.EqualPictureInterval);
            Assert.Equal(model, timing.DecoderModel.HasValue);
            if (equalInterval) { Assert.Equal(new FrameRate(30000, 1001), timing.Info.FrameRate); }
            else { Assert.Null(timing.Info.FrameRate); }
            if (model)
            {
                Assert.Equal(new Av1DecoderModelInfo(16, 1001, 10, 10), timing.DecoderModel);
                Assert.Equal(1, timing.OperatingPoints.Length);
                Assert.Equal(45000U, timing.OperatingPoints[0].DecoderBufferDelay);
                Assert.Equal(45000U, timing.OperatingPoints[0].EncoderBufferDelay);
            }
            Av1SequenceHeader again = Sequence(fixture);
            Assert.Equal(header, again);
            Assert.Equal(timing.GetHashCode(), again.Timing!.GetHashCode());
        }

        [Fact]
        public void ReadsModelTimesInCodedAndReferenceDisplayPictures()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "timingModel.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> packets = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            int visible = 0;
            int coded = 0;
            int displayed = 0;
            ReadOnlySpan<uint> presentationTimes = [0, 1, 1, 3, 4, 5];
            using (Av1PictureDecoder decoder = new(allocator))
            {
                foreach (ReadOnlyMemory<byte> packet in packets)
                {
                    Av1FrameUnitReader units = new(packet.Span);
                    while (units.TryRead(out ReadOnlySpan<byte> unit, CancellationToken.None))
                    {
                        using Av1Picture picture = decoder.Decode(unit);
                        if (picture.Header.Timing.BufferRemovalTime is uint time)
                        {
                            Assert.Equal((uint)coded, time);
                            coded++;
                        }
                        else { displayed++; }
                        if (picture.Header.ShowFrame)
                        {
                            Assert.Equal(presentationTimes[visible], picture.Header.Timing.PresentationTime);
                            visible++;
                        }
                        else { Assert.Null(picture.Header.Timing.PresentationTime); }
                    }
                }
            }
            Assert.Equal(6, visible);
            Assert.Equal(7, coded);
            Assert.Equal(1, displayed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("1", 0U)]
        [InlineData("010", 1U)]
        [InlineData("011", 2U)]
        [InlineData("00100", 3U)]
        [InlineData("00111", 6U)]
        public void ReadsUvlcGoldenCodes(string encoded, uint expected)
        {
            Av1BitReader bits = Reader(encoded);
            Assert.Equal(expected, bits.ReadUvlc());
        }

        [Fact]
        public void UvlcHandlesMaximumValueAndConsumesTheEntireSentinelPrefix()
        {
            Av1BitReader maximum = Reader(new string('0', 31) + "1" + new string('1', 31));
            Assert.Equal(uint.MaxValue - 1, maximum.ReadUvlc());
            Av1BitReader sentinel = Reader(new string('0', 33) + "1101");
            Assert.Equal(uint.MaxValue, sentinel.ReadUvlc());
            Assert.Equal(5, sentinel.Read(3));
            Assert.Throws<InvalidDataException>(() => ReadUvlc(new byte[8]));
        }

        [Fact]
        public void UnsignedValuesKeepAll32BitsAtUnalignedPositions()
        {
            Av1BitReader bits = Reader("101" + new string('1', 32) + "011");
            Assert.Equal(5, bits.Read(3));
            Assert.Equal(uint.MaxValue, bits.ReadUnsigned(32));
            Assert.Equal(3, bits.Read(3));
        }

        [Theory]
        [InlineData(0U, 1U)]
        [InlineData(1U, 0U)]
        public void RejectsZeroClockUnits(uint units, uint scale) =>
            Assert.Throws<InvalidDataException>(() => ReadTiming(Unsigned(units, 32) + Unsigned(scale, 32) + "0"));

        [Fact]
        public void RejectsUvlcSentinelAsPictureInterval() =>
            Assert.Throws<InvalidDataException>(() => ReadTiming(Unsigned(1, 32) + Unsigned(1, 32) + "1" + new string('0', 32) + "1"));

        [Fact]
        public void ExactCadenceUsesFullUnsignedProduct()
        {
            Av1TimingInfo timing = ReadTiming(Unsigned(uint.MaxValue, 32) + Unsigned(uint.MaxValue, 32) + "1" +
                new string('0', 31) + "1" + new string('1', 31));
            Assert.Equal(new FrameRate(1, uint.MaxValue), timing.FrameRate);
        }

        [Fact]
        public void BaseLayerRemovalTimesFollowEveryApplicableOperatingPoint()
        {
            string prefix = "1" + Unsigned(1, 32) + Unsigned(25, 32) + "0" + "1" +
                Unsigned(31, 5) + Unsigned(1, 32) + Unsigned(31, 5) + Unsigned(31, 5) + "1" + Unsigned(2, 5);
            string points = Point(0, uint.MaxValue, 1) + Point(0x202, 2, 2) + Point(0x101, 3, 3);
            Av1BitReader bits = Reader(prefix + points + "1" + Unsigned(uint.MaxValue, 32) + Unsigned(123, 32) + "101");
            Av1SequenceTiming timing = Assert.IsType<Av1SequenceTiming>(Av1SequenceTiming.Read(ref bits, out int level, out bool tier));
            Assert.Equal(0, level);
            Assert.False(tier);
            Assert.Equal(3, timing.OperatingPoints.Length);
            Assert.Equal(uint.MaxValue, timing.OperatingPoints[0].DecoderBufferDelay);
            Assert.Equal(3, timing.OperatingPoints[2].InitialDisplayDelay);
            Assert.Equal(uint.MaxValue, timing.ReadBufferRemovalTime(ref bits));
            Assert.Equal(5, bits.Read(3));
        }

        private static Av1SequenceHeader Sequence(string fixture)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", fixture + ".obu"));
            Av1ObuReader obus = new(encoded);
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == 1) { return Av1SequenceHeader.Parse(payload); }
            }
            throw new InvalidDataException("The fixture has no sequence header.");
        }

        private static string Point(uint idc, uint delay, uint displayDelay) => Unsigned(idc, 12) + Unsigned(0, 5) + "1" +
            Unsigned(delay, 32) + Unsigned(delay, 32) + "0" + "1" + Unsigned(displayDelay - 1, 4);

        private static string Unsigned(uint value, int count) => Convert.ToString(value, 2).PadLeft(count, '0');

        private static Av1BitReader Reader(string encoded)
        {
            byte[] bytes = new byte[(encoded.Length + 7) / 8];
            for (int index = 0; index < encoded.Length; index++)
            {
                if (encoded[index] == '1') { bytes[index >> 3] |= (byte)(1 << (7 - (index & 7))); }
            }
            return new(bytes);
        }

        private static uint ReadUvlc(byte[] data)
        {
            Av1BitReader bits = new(data);
            return bits.ReadUvlc();
        }

        private static Av1TimingInfo ReadTiming(string encoded)
        {
            Av1BitReader bits = Reader(encoded);
            return Av1TimingInfo.Read(ref bits);
        }
    }
}
