using System.Numerics;
using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacSynthesisTests
    {
        [Theory]
        [InlineData(128)]
        [InlineData(1024)]
        public void FftImdctMatchesTheDirectTransform(int length)
        {
            double[] spectrum = Enumerable.Range(0, length).Select(index => Math.Sin(index * 1.7) * (index % 7 - 3)).ToArray();
            double[] output = new double[length * 2];
            Complex[] work = Enumerable.Repeat(new Complex(double.NaN, double.NaN), length / 2).ToArray();
            AacImdct.Transform(spectrum, output, work, CancellationToken.None);
            for (int sample = 0; sample < output.Length; sample++)
            {
                double expected = 0;
                for (int coefficient = 0; coefficient < length; coefficient++)
                {
                    expected += spectrum[coefficient] * Math.Cos(Math.PI / length * (sample + 0.5 + length / 2) * (coefficient + 0.5));
                }
                Assert.InRange(Math.Abs(output[sample] - expected / length), 0, 2e-12);
            }
        }

        [Theory]
        [InlineData(128)]
        [InlineData(1024)]
        public void ImdctBasisVectorsPreserveEvenOddAndBoundaryCoefficients(int length)
        {
            double[] spectrum = new double[length];
            double[] output = new double[length * 2 + 1];
            Complex[] work = new Complex[length / 2];
            output[^1] = 123;
            foreach (int coefficient in new[] { 0, 1, length / 2 - 1, length / 2, length - 2, length - 1 })
            {
                spectrum.AsSpan().Clear();
                spectrum[coefficient] = -length;
                AacImdct.Transform(spectrum, output, work, CancellationToken.None);
                for (int sample = 0; sample < length * 2; sample++)
                {
                    double expected = -Math.Cos(Math.PI / length * (sample + 0.5 + length / 2) * (coefficient + 0.5));
                    Assert.InRange(Math.Abs(output[sample] - expected), 0, 2e-12);
                }
                Assert.Equal(123, output[^1]);
            }
        }

        [Theory]
        [InlineData(128, 0)]
        [InlineData(1024, 0)]
        [InlineData(128, 1)]
        [InlineData(1024, 1)]
        public void WindowHalvesMeetThePerfectReconstructionCondition(int length, int shape)
        {
            ReadOnlySpan<double> window = AacWindows.Get(length, shape);
            for (int index = 0; index < length; index++)
            {
                Assert.InRange(window[index], double.Epsilon, 1);
                Assert.InRange(Math.Abs(window[index] * window[index] + window[length - 1 - index] * window[length - 1 - index] - 1), 0, 2e-15);
                if (index > 0)
                {
                    Assert.True(window[index] > window[index - 1]);
                }
            }
            if (shape == 0)
            {
                Assert.Equal(Math.Sin(Math.PI / (4 * length)), window[0]);
            }
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0, 0, 1)]
        [InlineData(0, 1, 0)]
        [InlineData(0, 1, 1)]
        [InlineData(1, 0, 0)]
        [InlineData(1, 0, 1)]
        [InlineData(1, 1, 0)]
        [InlineData(1, 1, 1)]
        [InlineData(2, 0, 0)]
        [InlineData(2, 0, 1)]
        [InlineData(2, 1, 0)]
        [InlineData(2, 1, 1)]
        [InlineData(3, 0, 0)]
        [InlineData(3, 0, 1)]
        [InlineData(3, 1, 0)]
        [InlineData(3, 1, 1)]
        public void WindowSwitchingAndOverlapRetainTheCompleteTail(int sequence, int shape, int previousShape)
        {
            double[] spectrum = new double[1024];
            double[] expected = new double[2048];
            if (sequence == 2)
            {
                for (int window = 0; window < 8; window++)
                {
                    spectrum[window * 128] = 128 * 32768;
                    ReadOnlySpan<double> left = AacWindows.Get(128, window == 0 ? previousShape : shape);
                    ReadOnlySpan<double> right = AacWindows.Get(128, shape);
                    for (int index = 0; index < 128; index++)
                    {
                        expected[448 + window * 128 + index] += Math.Cos(Math.PI / 128 * (index + 64.5) * 0.5) * left[index];
                        expected[576 + window * 128 + index] += Math.Cos(Math.PI / 128 * (index + 192.5) * 0.5) * right[127 - index];
                    }
                }
            }
            else
            {
                spectrum[0] = 1024 * 32768;
                for (int index = 0; index < 1024; index++)
                {
                    double left = sequence == 3 ? StopWindow(index, previousShape) : AacWindows.Get(1024, previousShape)[index];
                    double right = sequence == 1 ? StartWindow(index, shape) : AacWindows.Get(1024, shape)[1023 - index];
                    expected[index] = Math.Cos(Math.PI / 1024 * (index + 512.5) * 0.5) * left;
                    expected[1024 + index] = Math.Cos(Math.PI / 1024 * (index + 1536.5) * 0.5) * right;
                }
            }
            double[] overlap = new double[1024];
            double[] output = new double[1024];
            double[] time = new double[2048];
            Complex[] work = new Complex[2048];
            AacSynthesis.Process(spectrum, sequence, shape, previousShape, overlap, output, time, work, CancellationToken.None);
            for (int index = 0; index < 1024; index++)
            {
                Assert.InRange(Math.Abs(expected[index] - output[index]), 0, 2e-14);
            }
            AacSynthesis.Process(new double[1024], 0, 0, shape, overlap, output, time, work, CancellationToken.None);
            for (int index = 0; index < 1024; index++)
            {
                Assert.InRange(Math.Abs(expected[1024 + index] - output[index]), 0, 2e-14);
                Assert.Equal(0, overlap[index]);
            }
        }

        [Fact]
        public void ImdctObservesCancellation()
        {
            using CancellationTokenSource source = new();
            source.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                AacImdct.Transform(new double[1024], new double[2048], new Complex[2048], source.Token));
        }

        private static double StopWindow(int index, int shape) => index switch
        {
            < 448 => 0,
            < 576 => AacWindows.Get(128, shape)[index - 448],
            _ => 1
        };

        private static double StartWindow(int index, int shape) => index switch
        {
            < 448 => 1,
            < 576 => AacWindows.Get(128, shape)[575 - index],
            _ => 0
        };
    }
}
