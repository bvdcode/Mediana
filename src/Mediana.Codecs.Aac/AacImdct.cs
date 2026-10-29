using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacImdct
    {
        public static void Transform(ReadOnlySpan<double> spectrum, Span<double> output, Span<Complex> work,
            CancellationToken cancellationToken)
        {
            int size = spectrum.Length;
            if (size is not (128 or 1024) || output.Length < 2 * size || work.Length < 2 * size)
            {
                throw new ArgumentException("AAC IMDCT requires a supported transform size and sufficient workspace.");
            }
            work = work[..(2 * size)];
            work.Clear();
            double phase = Math.PI / (2 * size);
            for (int index = 0; index < size; index++)
            {
                (double sine, double cosine) = Math.SinCos(-phase * index);
                work[index] = spectrum[index] * new Complex(cosine, sine);
            }
            Fourier(work, cancellationToken);
            for (int index = 0; index < size; index++)
            {
                (double sine, double cosine) = Math.SinCos(-phase * (index + 0.5));
                work[index] = new Complex((work[index] * new Complex(cosine, sine)).Real / size, 0);
            }
            for (int index = 0; index < size / 2; index++)
            {
                output[index] = work[size / 2 + index].Real;
                output[index + 3 * size / 2] = -work[index].Real;
            }
            for (int index = 0; index < size; index++)
            {
                output[size / 2 + index] = -work[size - 1 - index].Real;
            }
        }

        private static void Fourier(Span<Complex> values, CancellationToken cancellationToken)
        {
            int reversed = 0;
            for (int index = 1; index < values.Length; index++)
            {
                int bit = values.Length / 2;
                while ((reversed & bit) != 0)
                {
                    reversed ^= bit;
                    bit >>= 1;
                }
                reversed ^= bit;
                if (index < reversed)
                {
                    (values[index], values[reversed]) = (values[reversed], values[index]);
                }
            }
            for (int width = 2; width <= values.Length; width *= 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (double sine, double cosine) = Math.SinCos(-2 * Math.PI / width);
                Complex rotation = new(cosine, sine);
                for (int start = 0; start < values.Length; start += width)
                {
                    Complex factor = Complex.One;
                    for (int offset = 0; offset < width / 2; offset++)
                    {
                        int first = start + offset;
                        int second = first + width / 2;
                        Complex difference = factor * values[second];
                        Complex original = values[first];
                        values[first] = original + difference;
                        values[second] = original - difference;
                        factor *= rotation;
                    }
                }
            }
        }
    }
}
