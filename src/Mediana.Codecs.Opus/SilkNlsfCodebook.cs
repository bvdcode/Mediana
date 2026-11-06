// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct SilkNlsfCodebook
    {
        public SilkNlsfCodebook(int order)
        {
            if (order is not (10 or 16)) { throw new ArgumentOutOfRangeException(nameof(order)); }
            Order = order;
        }

        public int Order { get; }
        public int QuantizationStepQ16 => Order switch
        {
            10 => 11796,
            16 => 9830,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<byte> Centers => Order switch
        {
            10 => SilkNlsfNarrowTables.Centers,
            16 => SilkNlsfWideTables.Centers,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<short> Weights => Order switch
        {
            10 => SilkNlsfNarrowTables.Weights,
            16 => SilkNlsfWideTables.Weights,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<byte> FirstModel => Order switch
        {
            10 => SilkNlsfNarrowTables.FirstModel,
            16 => SilkNlsfWideTables.FirstModel,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<byte> Predictors => Order switch
        {
            10 => SilkNlsfNarrowTables.Predictors,
            16 => SilkNlsfWideTables.Predictors,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<byte> Selectors => Order switch
        {
            10 => SilkNlsfNarrowTables.Selectors,
            16 => SilkNlsfWideTables.Selectors,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<byte> ResidualModels => Order switch
        {
            10 => SilkNlsfNarrowTables.ResidualModels,
            16 => SilkNlsfWideTables.ResidualModels,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public ReadOnlySpan<short> MinimumDistances => Order switch
        {
            10 => SilkNlsfNarrowTables.MinimumDistances,
            16 => SilkNlsfWideTables.MinimumDistances,
            _ => throw new InvalidOperationException("The SILK LSF codebook is uninitialized.")
        };

        public void Unpack(int firstIndex, Span<byte> modelOffsets, Span<byte> predictors)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(firstIndex, 31);
            ArgumentOutOfRangeException.ThrowIfLessThan(modelOffsets.Length, Order, nameof(modelOffsets));
            ArgumentOutOfRangeException.ThrowIfLessThan(predictors.Length, Order, nameof(predictors));
            ReadOnlySpan<byte> selectors = Selectors.Slice(firstIndex * Order / 2, Order / 2), coefficients = Predictors;
            for (int at = 0; at < Order; at += 2)
            {
                byte selector = selectors[at / 2];
                modelOffsets[at] = (byte)(9 * ((selector >> 1) & 7));
                modelOffsets[at + 1] = (byte)(9 * ((selector >> 5) & 7));
                predictors[at] = coefficients[at + (selector & 1) * (Order - 1)];
                predictors[at + 1] = coefficients[at + 1 + ((selector >> 4) & 1) * (Order - 1)];
            }
        }
    }
}
