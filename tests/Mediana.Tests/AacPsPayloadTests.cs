// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacPsPayloadTests
    {
        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 20)]
        [InlineData(2, 34)]
        [InlineData(3, 10)]
        [InlineData(4, 20)]
        [InlineData(5, 34)]
        public void AllParameterModesReconstructFrequencyAndTimeDeltas(int mode, int bands)
        {
            AacPsData data = new();
            AacTestBitWriter first = Header(mode, mode, false).Write(0, 1).Write(1, 2).Write(0, 1);
            WriteWord(first, mode < 3 ? "1100" : "0001");
            Repeat(first, mode < 3 ? "0" : "1", bands - 1);
            first.Write(0, 1);
            WriteWord(first, "111110");
            Repeat(first, "0", bands - 1);
            Read(data, first.ToArray());
            Assert.True(data.Independent);
            Assert.Equal(1, data.EnvelopeCount);
            Assert.Equal(32, data.Envelopes[0].EndSlot);
            Assert.All(data.Envelopes[0].Intensity[..bands], value => Assert.Equal(2, value));
            Assert.All(data.Envelopes[0].Coherence[..bands], value => Assert.Equal(3, value));
            AacTestBitWriter second = new AacTestBitWriter().Write(0, 1).Write(0, 1).Write(1, 2).Write(1, 1);
            Repeat(second, mode < 3 ? "110" : "00", bands);
            second.Write(1, 1);
            Repeat(second, "110", bands);
            Read(data, second.ToArray());
            Assert.False(data.Independent);
            Assert.All(data.Envelopes[0].Intensity[..bands], value => Assert.Equal(3, value));
            Assert.All(data.Envelopes[0].Coherence[..bands], value => Assert.Equal(2, value));
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(1, 11)]
        [InlineData(2, 17)]
        public void PhaseDeltasWrapModuloEightWithinTheExtensionBoundary(int mode, int bands)
        {
            AacPsData data = new();
            AacTestBitWriter first = Header(mode, null, true).Write(0, 1).Write(1, 2).Write(0, 1);
            Repeat(first, "0", AacPsHeader.ParameterCount(mode));
            AacTestBitWriter extension = new AacTestBitWriter().Write(0, 2).Write(1, 1).Write(0, 1);
            WriteWord(extension, "0111");
            Repeat(extension, "1", bands - 1);
            extension.Write(0, 1);
            WriteWord(extension, "000");
            Repeat(extension, "1", bands - 1);
            extension.Write(0, 1);
            AppendExtension(first, extension);
            Read(data, first.ToArray());
            Assert.True(data.Independent);
            Assert.True(data.PhaseEnabled);
            Assert.All(data.Envelopes[0].PhaseDifference[..bands], value => Assert.Equal(7, value));
            Assert.All(data.Envelopes[0].OverallPhase[..bands], value => Assert.Equal(7, value));
            AacTestBitWriter second = new AacTestBitWriter().Write(0, 1).Write(0, 1).Write(1, 2).Write(1, 1);
            Repeat(second, "0", AacPsHeader.ParameterCount(mode));
            extension = new AacTestBitWriter().Write(0, 2).Write(1, 1).Write(1, 1);
            Repeat(extension, "010", bands);
            extension.Write(1, 1);
            Repeat(extension, "010", bands);
            extension.Write(0, 1);
            AppendExtension(second, extension);
            Read(data, second.ToArray());
            Assert.False(data.Independent);
            Assert.All(data.Envelopes[0].PhaseDifference[..bands], value => Assert.Equal(0, value));
            Assert.All(data.Envelopes[0].OverallPhase[..bands], value => Assert.Equal(0, value));
        }

        [Fact]
        public void VariableBordersAppendAHeldTailAndEmptyFramesKeepEnabledParameters()
        {
            AacPsData data = new();
            AacTestBitWriter first = Header(0, null, false).Write(1, 1).Write(1, 2).Write(7, 5).Write(20, 5).Write(0, 1);
            WriteWord(first, "100");
            Repeat(first, "0", 9);
            first.Write(1, 1);
            Repeat(first, "110", 10);
            Read(data, first.ToArray());
            Assert.Equal(2, data.TransmittedEnvelopeCount);
            Assert.Equal(3, data.EnvelopeCount);
            Assert.Equal(new[] { 8, 21, 32 }, data.Envelopes.Take(3).Select(envelope => envelope.EndSlot));
            Assert.All(data.Envelopes[2].Intensity[..10], value => Assert.Equal(2, value));
            Read(data, new AacTestBitWriter().Write(0, 1).Write(0, 1).Write(0, 2).ToArray());
            Assert.Equal(0, data.TransmittedEnvelopeCount);
            Assert.Equal(1, data.EnvelopeCount);
            Assert.All(data.Envelopes[0].Intensity[..10], value => Assert.Equal(2, value));
            Read(data, Header(null, null, false).Write(0, 1).Write(0, 2).ToArray());
            Assert.All(data.Envelopes[0].Intensity, value => Assert.Equal(0, value));
        }

        [Fact]
        public void EmptyDisabledParametersBecomeTheNextTimeDifferentialHistory()
        {
            AacPsData data = new();
            AacTestBitWriter first = Header(0, null, false).Write(0, 1).Write(1, 2).Write(0, 1);
            WriteWord(first, "1100");
            Repeat(first, "0", 9);
            Read(data, first.ToArray());
            Read(data, Header(null, null, false).Write(0, 1).Write(0, 2).ToArray());
            AacTestBitWriter following = Header(0, null, false).Write(0, 1).Write(1, 2).Write(1, 1);
            Repeat(following, "0", 10);
            Read(data, following.ToArray());
            Assert.All(data.Envelopes[0].Intensity, value => Assert.Equal(0, value));
        }

        [Fact]
        public void ModeChangesCannotUseOldDifferentialHistory()
        {
            AacPsData data = new();
            AacTestBitWriter first = Header(0, null, false).Write(0, 1).Write(1, 2).Write(0, 1);
            Repeat(first, "0", 10);
            Read(data, first.ToArray());
            byte[] changed = Header(1, null, false).Write(0, 1).Write(1, 2).Write(1, 1).ToArray();
            Assert.Throws<InvalidDataException>(() => Read(data, changed));
        }

        [Theory]
        [InlineData(6)]
        [InlineData(7)]
        public void ReservedHeaderModesAreRejected(int mode)
        {
            byte[] invalid = Header(mode, null, false).ToArray();
            Assert.Throws<InvalidDataException>(() => Read(new AacPsData(), invalid));
        }

        [Fact]
        public void OverlappingBordersAndOutOfRangeParametersAreRejected()
        {
            byte[] borders = Header(null, null, false).Write(1, 1).Write(1, 2).Write(9, 5).Write(9, 5).ToArray();
            Assert.Throws<InvalidDataException>(() => Read(new AacPsData(), borders));
            AacTestBitWriter invalid = Header(0, null, false).Write(0, 1).Write(1, 2).Write(0, 1);
            WriteWord(invalid, "1111111111100");
            Assert.Throws<InvalidDataException>(() => Read(new AacPsData(), invalid.ToArray()));
        }

        [Fact]
        public void APhaseExtensionCannotReadPastItsDeclaredExtent()
        {
            AacTestBitWriter invalid = Header(2, null, true).Write(0, 1).Write(1, 2).Write(0, 1);
            Repeat(invalid, "0", 34);
            invalid.Write(1, 4).Write(0b00100000, 8);
            Repeat(invalid, "0", 100);
            Assert.Throws<InvalidDataException>(() => Read(new AacPsData(), invalid.ToArray()));
        }

        private static AacTestBitWriter Header(int? intensity, int? coherence, bool extension)
        {
            AacTestBitWriter writer = new AacTestBitWriter().Write(1, 1).Write(intensity.HasValue ? 1 : 0, 1);
            if (intensity.HasValue)
            {
                writer.Write(intensity.Value, 3);
            }
            writer.Write(coherence.HasValue ? 1 : 0, 1);
            if (coherence.HasValue)
            {
                writer.Write(coherence.Value, 3);
            }
            return writer.Write(extension ? 1 : 0, 1);
        }

        private static void Read(AacPsData data, byte[] bytes)
        {
            AacBitReader bits = new(bytes);
            data.Read(ref bits);
        }

        private static void AppendExtension(AacTestBitWriter writer, AacTestBitWriter extension)
        {
            byte[] bytes = extension.ToArray();
            writer.Write(bytes.Length, 4);
            foreach (byte value in bytes)
            {
                writer.Write(value, 8);
            }
        }

        private static void WriteWord(AacTestBitWriter writer, string word)
        {
            foreach (char bit in word)
            {
                writer.Write(bit - '0', 1);
            }
        }

        private static void Repeat(AacTestBitWriter writer, string word, int count)
        {
            for (int index = 0; index < count; index++)
            {
                WriteWord(writer, word);
            }
        }
    }
}
