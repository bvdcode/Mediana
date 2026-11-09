// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacPsSignalTests
    {
        [Theory]
        [InlineData(20)]
        [InlineData(34)]
        public void HybridAnalysisAndSummingSynthesisRecoverAllComplexQmfSlots(int bands)
        {
            Complex[] input = new Complex[38 * 64];
            Complex[] history = new Complex[6 * 5];
            for (int index = 0; index < input.Length; index++)
            {
                input[index] = new Complex(Math.Sin(index * 0.13), Math.Cos(index * 0.41));
            }
            for (int index = 0; index < history.Length; index++)
            {
                history[index] = new Complex(Math.Sin(index * 0.27), Math.Cos(index * 0.53));
            }
            Complex[] hybrid = new Complex[91];
            Complex[] reconstructed = new Complex[64];
            for (int slot = 0; slot < 32; slot++)
            {
                AacPsHybrid.Analyze(input, history, slot, bands, hybrid);
                AacPsHybrid.Synthesize(hybrid, bands, reconstructed);
                for (int band = 0; band < 64; band++)
                {
                    Assert.True(Complex.Abs(input[slot * 64 + band] - reconstructed[band]) < 0.000000000001);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void BothMixingProceduresPreserveTotalEnergyAndIidAtEveryCoherence(int mode)
        {
            AacPsData data = Parameters(mode, mode, false);
            Complex[] coefficients = new Complex[AacPsMixing.CoefficientCount];
            Complex[] phases = new Complex[AacPsMixing.CoefficientCount];
            double[] real = new double[AacPsMixing.CoefficientCount];
            foreach (int intensity in new[] { -3, 0, 3 })
            {
                Array.Fill(data.Envelopes[0].Intensity, intensity);
                for (int coherence = 0; coherence < 8; coherence++)
                {
                    Array.Fill(data.Envelopes[0].Coherence, coherence);
                    AacPsMixing.Calculate(data, data.Envelopes[0], coefficients, phases, real);
                    double left = real[0] * real[0] + real[68] * real[68];
                    double right = real[34] * real[34] + real[102] * real[102];
                    double expectedDecibels = mode < 3 ? intensity * 7.0 / 3 : intensity * 2.0;
                    Assert.Equal(2, left + right, 12);
                    Assert.Equal(expectedDecibels, 10 * Math.Log10(left / right), 10);
                }
            }
        }

        [Fact]
        public void PhaseSmoothingUsesTwoPreviousAnglesAndDoesNotAdvanceWhenDisabled()
        {
            AacPsData data = Parameters(2, 2, true);
            data.Envelopes[0].PhaseDifference[0] = 2;
            data.Envelopes[0].OverallPhase[0] = 4;
            Complex[] coefficients = new Complex[AacPsMixing.CoefficientCount];
            Complex[] phases = Enumerable.Repeat(Complex.One, AacPsMixing.CoefficientCount).ToArray();
            double[] real = new double[AacPsMixing.CoefficientCount];
            AacPsMixing.Calculate(data, data.Envelopes[0], coefficients, phases, real);
            Assert.True(Complex.Abs(coefficients[0] - new Complex(-1, 0)) < 0.000000000001);
            Assert.True(Complex.Abs(coefficients[34] - new Complex(-0.6, 0.8)) < 0.000000000001);
            Complex[] expected = phases.ToArray();
            AacPsData disabled = Parameters(2, 2, false);
            AacPsMixing.Calculate(disabled, disabled.Envelopes[0], coefficients, phases, real);
            Assert.Equal(expected, phases);
            Assert.All(coefficients, value => Assert.Equal(0, value.Imaginary));
        }

        [Fact]
        public void DisablingBothParameterSetsKeepsPreviousHybridResolution()
        {
            AacPsData data = Parameters(5, 5, false);
            Assert.Equal(34, data.StereoBands);
            AacTestBitWriter empty = new AacTestBitWriter().Write(1, 1).Write(0, 1).Write(0, 1).Write(0, 1).Write(0, 1).Write(0, 2);
            AacBitReader bits = new(empty.ToArray());
            data.Read(ref bits);
            Assert.Equal(34, data.StereoBands);
            Assert.Equal(0, data.TransmittedEnvelopeCount);
            Assert.All(data.Envelopes[0].Intensity, value => Assert.Equal(0, value));
            Assert.All(data.Envelopes[0].Coherence, value => Assert.Equal(0, value));
        }

        [Fact]
        public void ParameterMappingUsesWeightedBoundaryBandsAndSignedAliasIndices()
        {
            double[] source = Enumerable.Range(0, 34).Select(index => (double)index).ToArray();
            double[] mapped = new double[34];
            AacPsBandMapping.Map<double>(source, 34, mapped, 20);
            Assert.Equal(1.0 / 3, mapped[0], 12);
            Assert.Equal(5.0 / 3, mapped[1], 12);
            Assert.Equal(29.5, mapped[18]);
            Assert.Equal(32.5, mapped[19]);
            ReadOnlySpan<sbyte> twenty = AacPsBandMapping.Parameters(20);
            ReadOnlySpan<sbyte> thirtyFour = AacPsBandMapping.Parameters(34);
            Assert.Equal(new sbyte[] { -2, -1 }, twenty[..2].ToArray());
            Assert.Equal(new sbyte[] { -3, -2, -1, 10, 10 }, thirtyFour.Slice(9, 5).ToArray());
        }

        private static AacPsData Parameters(int intensityMode, int coherenceMode, bool phase)
        {
            AacTestBitWriter writer = new AacTestBitWriter().Write(1, 1).Write(1, 1).Write(intensityMode, 3)
                .Write(1, 1).Write(coherenceMode, 3).Write(phase ? 1 : 0, 1).Write(0, 1).Write(1, 2).Write(0, 1);
            int intensityBands = AacPsHeader.ParameterCount(intensityMode);
            for (int band = 0; band < intensityBands; band++)
            {
                writer.Write(intensityMode < 3 ? 0 : 1, 1);
            }
            writer.Write(0, 1);
            for (int band = 0; band < AacPsHeader.ParameterCount(coherenceMode); band++)
            {
                writer.Write(0, 1);
            }
            if (phase)
            {
                AacTestBitWriter extension = new AacTestBitWriter().Write(0, 2).Write(1, 1).Write(0, 1);
                int count = AacPsHeader.PhaseCount(intensityMode);
                for (int band = 0; band < count; band++)
                {
                    extension.Write(1, 1);
                }
                extension.Write(0, 1);
                for (int band = 0; band < count; band++)
                {
                    extension.Write(1, 1);
                }
                extension.Write(0, 1);
                byte[] bytes = extension.ToArray();
                writer.Write(bytes.Length, 4);
                foreach (byte value in bytes)
                {
                    writer.Write(value, 8);
                }
            }
            AacPsData result = new();
            AacBitReader bits = new(writer.ToArray());
            result.Read(ref bits);
            return result;
        }
    }
}
