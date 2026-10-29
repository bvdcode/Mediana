using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacImdct
    {
        private static readonly AacImdctPlan Short = new(128);
        private static readonly AacImdctPlan Long = new(1024);

        public static void Transform(ReadOnlySpan<double> spectrum, Span<double> output, Span<Complex> work,
            CancellationToken cancellationToken)
        {
            int size = spectrum.Length;
            if (size is not (128 or 1024) || output.Length < 2 * size || work.Length < size / 2)
            {
                throw new ArgumentException("AAC IMDCT requires a supported transform size and sufficient workspace.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            AacImdctPlan plan = size switch
            {
                128 => Short,
                1024 => Long,
                _ => throw new InvalidOperationException()
            };
            work = work[..(size / 2)];
            ReadOnlySpan<Complex> pre = plan.PreRotation;
            ReadOnlySpan<int> reversed = plan.Reversed;
            for (int index = 0; index < work.Length; index++)
            {
                double even = spectrum[2 * index];
                double odd = spectrum[size - 1 - 2 * index];
                Complex rotation = pre[index];
                work[reversed[index]] = new Complex(even * rotation.Real - odd * rotation.Imaginary,
                    even * rotation.Imaginary + odd * rotation.Real);
            }
            Fourier(work, plan.Rotations, cancellationToken);
            ReadOnlySpan<Complex> post = plan.PostRotation;
            for (int index = 0; index < work.Length; index++)
            {
                Complex value = work[index];
                Complex rotation = post[index];
                double even = value.Real * rotation.Real - value.Imaginary * rotation.Imaginary;
                double odd = -(value.Real * rotation.Imaginary + value.Imaginary * rotation.Real);
                WriteOutput(output, size, 2 * index, even);
                WriteOutput(output, size, size - 1 - 2 * index, odd);
            }
        }

        private static void WriteOutput(Span<double> output, int size, int coefficient, double value)
        {
            output[3 * size / 2 - 1 - coefficient] = -value;
            if (coefficient < size / 2)
            {
                output[3 * size / 2 + coefficient] = -value;
            }
            else
            {
                output[coefficient - size / 2] = value;
            }
        }

        private static void Fourier(Span<Complex> values, ReadOnlySpan<Complex> rotations, CancellationToken cancellationToken)
        {
            for (int width = 2; width <= values.Length; width *= 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int half = width / 2;
                ReadOnlySpan<Complex> stage = rotations.Slice(half - 1, half);
                for (int start = 0; start < values.Length; start += width)
                {
                    for (int offset = 0; offset < half; offset++)
                    {
                        int first = start + offset;
                        int second = first + half;
                        Complex factor = stage[offset];
                        Complex value = values[second];
                        double real = factor.Real * value.Real - factor.Imaginary * value.Imaginary;
                        double imaginary = factor.Real * value.Imaginary + factor.Imaginary * value.Real;
                        Complex original = values[first];
                        values[first] = new Complex(original.Real + real, original.Imaginary + imaginary);
                        values[second] = new Complex(original.Real - real, original.Imaginary - imaginary);
                    }
                }
            }
        }
    }
}
