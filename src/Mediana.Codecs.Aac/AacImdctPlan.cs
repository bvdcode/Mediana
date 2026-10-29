using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal class AacImdctPlan
    {
        public AacImdctPlan(int size)
        {
            int count = size / 2;
            PreRotation = new Complex[count];
            PostRotation = new Complex[count];
            Reversed = new int[count];
            Rotations = new Complex[count - 1];
            double phase = Math.PI / size;
            for (int index = 0; index < count; index++)
            {
                (double sine, double cosine) = Math.SinCos(-phase * index);
                PreRotation[index] = new Complex(cosine, sine);
                (sine, cosine) = Math.SinCos(-phase * (index + 0.25));
                PostRotation[index] = new Complex(cosine / size, sine / size);
                int reversed = 0;
                for (int value = index, bit = count / 2; bit != 0; bit >>= 1, value >>= 1)
                {
                    reversed |= (value & 1) * bit;
                }
                Reversed[index] = reversed;
            }
            for (int width = 2; width <= count; width *= 2)
            {
                for (int offset = 0; offset < width / 2; offset++)
                {
                    (double sine, double cosine) = Math.SinCos(-2 * Math.PI * offset / width);
                    Rotations[width / 2 - 1 + offset] = new Complex(cosine, sine);
                }
            }
        }

        public Complex[] PreRotation { get; }
        public Complex[] PostRotation { get; }
        public int[] Reversed { get; }
        public Complex[] Rotations { get; }
    }
}
