// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacSbrQmfTests
    {
        [Fact]
        public void FastModulationMatchesDirectMatricesOverIndependentRandomSequences()
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            using (AacSbrQmf fast = new(allocator))
            using (AacSbrQmfReference direct = new(allocator))
            {
                Random random = new(15279);
                double[] input = new double[32];
                Complex[] fastBands = new Complex[32];
                Complex[] directBands = new Complex[32];
                Complex[] synthesisInput = new Complex[64];
                double[] fastPcm = new double[64];
                double[] directPcm = new double[64];
                for (int slot = 0; slot < 96; slot++)
                {
                    for (int index = 0; index < input.Length; index++)
                    {
                        input[index] = random.NextDouble() * 2 - 1;
                    }
                    fast.Analyze(input, fastBands);
                    direct.Analyze(input, directBands);
                    for (int band = 0; band < fastBands.Length; band++)
                    {
                        Assert.InRange(Complex.Abs(fastBands[band] - directBands[band]), 0, 2e-8);
                    }
                    for (int band = 0; band < synthesisInput.Length; band++)
                    {
                        synthesisInput[band] = new Complex(random.NextDouble() * 65536 - 32768,
                            random.NextDouble() * 65536 - 32768);
                    }
                    fast.Synthesize(synthesisInput, fastPcm);
                    direct.Synthesize(synthesisInput, directPcm);
                    for (int index = 0; index < fastPcm.Length; index++)
                    {
                        Assert.InRange(Math.Abs(fastPcm[index] - directPcm[index]), 0, 2e-12);
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(32)]
        [InlineData(64)]
        public async Task CompleteImpulseResponseMatchesIndependentAnalysisAndSynthesis(int synthesisBands)
        {
            byte[] bands = await ReadReferenceAsync("bands");
            string outputName = synthesisBands switch
            {
                32 => "single-rate-out",
                64 => "out",
                _ => throw new ArgumentOutOfRangeException(nameof(synthesisBands))
            };
            byte[] pcm = await ReadReferenceAsync(outputName);
            Assert.Equal(96 * 32 * 2 * sizeof(float), bands.Length);
            Assert.Equal(96 * synthesisBands * sizeof(float), pcm.Length);
            MediaBufferAllocator allocator = new(32 * 1024);
            using (AacSbrQmf qmf = new(allocator, synthesisBands))
            {
                double[] input = new double[32];
                Complex[] output = new Complex[synthesisBands];
                double[] samples = new double[synthesisBands];
                double analysisError = 0;
                double analysisEnergy = 0;
                double synthesisError = 0;
                double synthesisEnergy = 0;
                for (int slot = 0; slot < 96; slot++)
                {
                    Array.Clear(input);
                    if (slot == 0)
                    {
                        input[13] = 1;
                    }
                    qmf.Analyze(input, output.AsSpan(0, 32));
                    for (int band = 0; band < 32; band++)
                    {
                        int offset = (slot * 32 + band) * 2 * sizeof(float);
                        Complex expected = new(BinaryPrimitives.ReadSingleLittleEndian(bands.AsSpan(offset)),
                            BinaryPrimitives.ReadSingleLittleEndian(bands.AsSpan(offset + sizeof(float))));
                        Complex difference = expected - output[band];
                        analysisError += difference.Real * difference.Real + difference.Imaginary * difference.Imaginary;
                        analysisEnergy += expected.Real * expected.Real + expected.Imaginary * expected.Imaginary;
                    }
                    qmf.Synthesize(output, samples);
                    for (int index = 0; index < synthesisBands; index++)
                    {
                        double expected = BinaryPrimitives.ReadSingleLittleEndian(pcm.AsSpan((slot * synthesisBands + index) * sizeof(float))) / 32768;
                        double difference = expected - samples[index];
                        Assert.InRange(Math.Abs(difference), 0, 3e-7);
                        synthesisError += difference * difference;
                        synthesisEnergy += expected * expected;
                    }
                }
                Assert.True(10 * Math.Log10(analysisEnergy / analysisError) > 130);
                Assert.True(10 * Math.Log10(synthesisEnergy / synthesisError) > 130);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static async Task<byte[]> ReadReferenceAsync(string name)
        {
            await using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr", $"qmf-impulse-{name}.f32le.gz"));
            await using GZipStream gzip = new(file, CompressionMode.Decompress);
            await using MemoryStream output = new();
            await gzip.CopyToAsync(output);
            return output.ToArray();
        }
    }
}
