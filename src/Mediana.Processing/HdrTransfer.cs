// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal static class HdrTransfer
    {
        private const int Intervals = 4096;
        private static readonly Lazy<double[]> PqTable = new(() => Create(DecodePq));
        private static readonly Lazy<double[]> HlgTable = new(() => Create(DecodeHlg));

        public static double[] Pq => PqTable.Value;
        public static double[] Hlg => HlgTable.Value;

        public static double Sample(double[] table, double value)
        {
            double coordinate = Math.Clamp(value, 0, 1) * Intervals;
            int index = Math.Min((int)coordinate, Intervals - 1);
            return table[index] + (table[index + 1] - table[index]) * (coordinate - index);
        }

        private static double[] Create(Func<double, double> decode)
        {
            double[] table = new double[Intervals + 1];
            for (int index = 0; index <= Intervals; index++) { table[index] = decode((double)index / Intervals); }
            return table;
        }

        // Rec. ITU-R BT.2100-3, Tables 4 and 5: PQ EOTF in cd/m² and inverse HLG OETF in scene light.
        private static double DecodePq(double value)
        {
            const double m1 = 2610.0 / 16384, m2 = 2523.0 / 32;
            const double c1 = 3424.0 / 4096, c2 = 2413.0 / 128, c3 = 2392.0 / 128;
            double power = Math.Pow(value, 1 / m2);
            return 10000 * Math.Pow(Math.Max(power - c1, 0) / (c2 - c3 * power), 1 / m1);
        }

        private static double DecodeHlg(double value)
        {
            const double a = 0.17883277, b = 1 - 4 * a;
            double c = 0.5 - a * Math.Log(4 * a);
            return value <= 0.5 ? value * value / 3 : (Math.Exp((value - c) / a) + b) / 12;
        }
    }
}
