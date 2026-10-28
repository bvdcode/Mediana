using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal class H264CabacResidualWriter(int qp, byte[] initialStates)
    {
        private readonly H264CabacIntervalWriter writer = Create(qp, initialStates);

        public void Write(int category, int neighbours, ReadOnlySpan<int> coefficients)
        {
            int[] significanceOffsets = [0, 15, 29, 44, 47];
            int[] levelOffsets = [0, 10, 20, 30, 39];
            int last = coefficients.Length - 1;
            while (last >= 0 && coefficients[last] == 0)
            {
                last--;
            }
            Bin(last >= 0, 85 + category * 4 + neighbours);
            if (last < 0)
            {
                return;
            }
            for (int index = 0; index <= last && index < coefficients.Length - 1; index++)
            {
                Bin(coefficients[index] != 0, 105 + significanceOffsets[category] + index);
                if (coefficients[index] != 0)
                {
                    Bin(index == last, 166 + significanceOffsets[category] + index);
                }
            }
            int equalOne = 0;
            int greaterOne = 0;
            for (int index = last; index >= 0; index--)
            {
                if (coefficients[index] == 0)
                {
                    continue;
                }
                int value = Math.Abs(coefficients[index]) - 1;
                int context = greaterOne != 0 ? 0 : Math.Min(4, 1 + equalOne);
                Bin(value != 0, 227 + levelOffsets[category] + context);
                for (int bin = 1; bin < Math.Min(14, value + 1); bin++)
                {
                    Bin(bin < value, 227 + levelOffsets[category] + 5 + Math.Min(category == 3 ? 3 : 4, greaterOne));
                }
                if (value >= 14)
                {
                    int remainder = value - 14;
                    int order = 0;
                    while (remainder >= 1 << order)
                    {
                        writer.Bits(1, 1);
                        remainder -= 1 << order++;
                    }
                    writer.Bits(0, 1);
                    writer.Bits((uint)remainder, order);
                }
                writer.Bits(coefficients[index] < 0 ? 1u : 0u, 1);
                if (value == 0)
                {
                    equalOne++;
                }
                else
                {
                    greaterOne++;
                }
            }
        }

        public byte[] Finish()
        {
            writer.Termination(true);
            return writer.ToEbsp();
        }

        private void Bin(bool value, int index) => writer.Decision(value, index, 0, 0);

        private static H264CabacIntervalWriter Create(int qp, byte[] initialStates)
        {
            H264CabacIntervalWriter result = new(qp);
            int[] indices = Enumerable.Range(3, 8).Concat(Enumerable.Range(60, 216)).Concat(Enumerable.Range(399, 3)).ToArray();
            for (int index = 0; index < indices.Length; index++)
            {
                result.Initialize(indices[index], initialStates[qp * indices.Length + index]);
            }
            return result;
        }
    }
}
