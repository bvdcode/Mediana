// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1GrainParameterTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EmptyScalingUsesInferredChromaAndConsumesOnlyItsSyntax(bool monochrome)
        {
            List<(int Value, int Count)> syntax = [(0, 4)];
            if (!monochrome) { syntax.Add((0, 1)); }
            syntax.AddRange([(3, 2), (3, 2), (3, 2), (3, 2), (1, 1), (1, 1), (0x5a, 8)]);
            Av1BitReader bits = new(Pack(syntax));
            Av1GrainParameters parameters = Av1GrainParameters.ReadUpdate(ref bits, ushort.MaxValue, monochrome);
            Assert.Empty(parameters.LumaPoints.ToArray());
            Assert.Empty(parameters.LumaCoefficients.ToArray());
            Assert.Empty(parameters.U.Points.ToArray());
            Assert.Empty(parameters.U.Coefficients.ToArray());
            Assert.Empty(parameters.V.Points.ToArray());
            Assert.False(parameters.ChromaScalingFromLuma);
            Assert.Equal(ushort.MaxValue, parameters.Seed);
            Assert.Equal(11, parameters.ScalingShift);
            Assert.Equal(3, parameters.Lag);
            Assert.Equal(9, parameters.CoefficientShift);
            Assert.Equal(3, parameters.GrainShift);
            Assert.True(parameters.Overlap);
            Assert.True(parameters.RestrictedRange);
            Assert.Equal(0x5a, bits.Read(8));
        }

        [Fact]
        public void ChromaFromLumaReadsSignedArCoefficientsWithoutChromaScalingPoints()
        {
            List<(int Value, int Count)> syntax = [(2, 4), (0, 8), (19, 8), (255, 8), (201, 8), (1, 1), (0, 2), (1, 2)];
            for (int channel = 0; channel < 3; channel++)
            {
                for (int index = 0; index < (channel == 0 ? 4 : 5); index++) { syntax.Add((index % 2 == 0 ? 0 : 255, 8)); }
            }
            syntax.AddRange([(0, 2), (0, 2), (0, 1), (1, 1), (0x79, 8)]);
            Av1BitReader bits = new(Pack(syntax));
            Av1GrainParameters parameters = Av1GrainParameters.ReadUpdate(ref bits, 7, false);
            Assert.True(parameters.ChromaScalingFromLuma);
            Assert.Equal(new sbyte[] { -128, 127, -128, 127 }, parameters.LumaCoefficients.ToArray());
            Assert.Equal(new sbyte[] { -128, 127, -128, 127, -128 }, parameters.U.Coefficients.ToArray());
            Assert.Equal(parameters.U.Coefficients.ToArray(), parameters.V.Coefficients.ToArray());
            Assert.Empty(parameters.U.Points.ToArray());
            Assert.Equal(0x79, bits.Read(8));
        }

        [Fact]
        public void RejectsInvalidCountsOrderAndAsymmetric420Chroma()
        {
            Reject(Pack([(15, 4)]));
            Reject(Pack([(2, 4), (15, 8), (12, 8), (15, 8), (13, 8)]));
            Reject(Pack([(2, 4), (16, 8), (12, 8), (15, 8), (13, 8)]));
            Reject(Pack([(1, 4), (1, 8), (1, 8), (0, 1), (11, 4)]));
            Reject(Pack([(1, 4), (1, 8), (1, 8), (0, 1), (1, 4), (0, 8), (0, 8), (0, 4)]));
            Reject([]);
        }

        private static void Reject(byte[] encoded) => Assert.Throws<InvalidDataException>(() =>
        {
            Av1BitReader bits = new(encoded);
            Av1GrainParameters.ReadUpdate(ref bits, 0, false);
        });

        private static byte[] Pack(IEnumerable<(int Value, int Count)> syntax)
        {
            List<byte> bytes = [];
            int value = 0, count = 0;
            foreach ((int field, int width) in syntax)
            {
                for (int index = width - 1; index >= 0; index--)
                {
                    value = (value << 1) | ((field >> index) & 1);
                    if (++count != 8) { continue; }
                    bytes.Add((byte)value);
                    value = 0;
                    count = 0;
                }
            }
            if (count != 0) { bytes.Add((byte)(value << (8 - count))); }
            return bytes.ToArray();
        }
    }
}
