// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal partial class H264CabacSyntax<TWriter>
    {
        public bool TryWriteResidual(H264ResidualCategory category, int plane, int x, int y,
            ReadOnlySpan<int> coefficients, out int count)
        {
            int scale = plane == 0 ? 16 : 8;
            int address = y / scale * target.Sequence.WidthInMbs + x / scale;
            bool intra = state.IsIntra(address);
            bool dc = category is H264ResidualCategory.Intra16LumaDc or H264ResidualCategory.ChromaDc;
            int context;
            if (dc)
            {
                context = state.DcCondition(state.Left(address), plane, intra) +
                    2 * state.DcCondition(state.Top(address), plane, intra);
            }
            else
            {
                Span<byte> counts = target.Counts(plane);
                int position = target.BlockIndex(plane, x, y);
                int unavailable = intra ? 1 : 0;
                int sampleScale = plane == 0 ? 1 : 2;
                context = !target.IsInCurrentSlice(x * sampleScale - 1, y * sampleScale) ? unavailable : counts[position - 1] != 0 ? 1 : 0;
                context += 2 * (!target.IsInCurrentSlice(x * sampleScale, y * sampleScale - 1) ? unavailable :
                    counts[position - target.Stride(plane) / 4] != 0 ? 1 : 0);
            }
            count = WriteResidual(category, context, coefficients);
            if (dc && count != 0) { state.DcFlags[address] |= (byte)(1 << plane); }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteResidualBin(bool value, int context) => writer.WriteBin(value, ref contexts[context + residualContextOffset]);

        private int WriteResidual(H264ResidualCategory category, int neighbourContext, ReadOnlySpan<int> coefficients)
        {
            (int count, int significantBase, int lastBase, int levelBase) = category switch
            {
                H264ResidualCategory.Intra16LumaDc => (16, 105, 166, 227),
                H264ResidualCategory.Intra16LumaAc => (15, 120, 181, 237),
                H264ResidualCategory.Luma4x4 => (16, 134, 195, 247),
                H264ResidualCategory.ChromaDc => (4, 149, 210, 257),
                H264ResidualCategory.ChromaAc => (15, 152, 213, 266),
                H264ResidualCategory.Luma8x8 => throw new NotSupportedException("CABAC encoding uses 4x4 transforms."),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            if (coefficients.Length != count) { throw new ArgumentException("Coefficient count does not match its CABAC category.", nameof(coefficients)); }
            uint nonzero = ClassifyCoefficients(coefficients);
            WriteResidualBin(nonzero != 0, 85 + (int)category * 4 + neighbourContext);
            if (nonzero == 0) { return 0; }
            int last = BitOperations.Log2(nonzero);
            int explicitCount = Math.Min(last + 1, count - 1);
            for (int index = 0; index < explicitCount; index++)
            {
                bool significant = (nonzero & (1u << index)) != 0;
                WriteResidualBin(significant, significantBase + index);
                if (significant)
                {
                    WriteResidualBin(index == last, lastBase + index);
                }
            }
            int equalOne = 0, greaterOne = 0;
            while (nonzero != 0)
            {
                int index = BitOperations.Log2(nonzero);
                nonzero ^= 1u << index;
                int signed = coefficients[index];
                int magnitude = Math.Abs(signed);
                int firstContext = greaterOne == 0 ? Math.Min(4, 1 + equalOne) : 0;
                WriteResidualBin(magnitude > 1, levelBase + firstContext);
                if (magnitude > 1)
                {
                    int continuation = levelBase + 5 + Math.Min(category == H264ResidualCategory.ChromaDc ? 3 : 4, greaterOne);
                    for (int prefix = 1; prefix < 14; prefix++)
                    {
                        WriteResidualBin(magnitude > prefix + 1, continuation);
                        if (magnitude == prefix + 1) { break; }
                    }
                    if (magnitude >= 15) { WriteExpGolomb(magnitude - 15, 0); }
                }
                writer.WriteBypass(signed < 0);
                if (magnitude == 1) { equalOne++; } else { greaterOne++; }
            }
            return equalOne + greaterOne;
        }

        private static uint ClassifyCoefficients(ReadOnlySpan<int> coefficients)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                ref int address = ref MemoryMarshal.GetReference(coefficients);
                Vector128<int> a = Vector128.LoadUnsafe(ref address);
                Vector128<int> minimum = Vector128.Create((int)short.MinValue);
                Vector128<uint> limit = Vector128.Create((uint)ushort.MaxValue);
                Vector128<uint> invalid = Vector128.GreaterThan((a - minimum).AsUInt32(), limit);
                uint mask = NonzeroMask(a);
                if (coefficients.Length != 4)
                {
                    Vector128<int> b = Vector128.LoadUnsafe(ref address, 4);
                    Vector128<int> c = Vector128.LoadUnsafe(ref address, 8);
                    int end = coefficients.Length - 4;
                    Vector128<int> d = Vector128.LoadUnsafe(ref address, (nuint)end);
                    invalid |= Vector128.GreaterThan((b - minimum).AsUInt32(), limit) |
                        Vector128.GreaterThan((c - minimum).AsUInt32(), limit) |
                        Vector128.GreaterThan((d - minimum).AsUInt32(), limit);
                    mask |= (NonzeroMask(b) << 4) | (NonzeroMask(c) << 8) | (NonzeroMask(d) << end);
                }
                if (invalid.ExtractMostSignificantBits() != 0)
                {
                    ThrowTransformPrecision();
                }
                return mask;
            }
            uint nonzero = 0;
            for (int index = 0; index < coefficients.Length; index++)
            {
                int level = coefficients[index];
                if (level is < short.MinValue or > short.MaxValue)
                {
                    ThrowTransformPrecision();
                }
                if (level != 0)
                {
                    nonzero |= 1u << index;
                }
            }
            return nonzero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint NonzeroMask(Vector128<int> values) =>
            Vector128.Equals(values, Vector128<int>.Zero).ExtractMostSignificantBits() ^ 15u;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTransformPrecision() =>
            throw new NotSupportedException("The H.264 CABAC coefficient exceeds 8-bit transform precision.");
    }
}
