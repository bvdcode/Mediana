// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

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
            int total = 0, last = -1;
            for (int index = 0; index < coefficients.Length; index++)
            {
                if (coefficients[index] == 0) { continue; }
                if (coefficients[index] is < short.MinValue or > short.MaxValue)
                { throw new NotSupportedException("The H.264 CABAC coefficient exceeds 8-bit transform precision."); }
                total++; last = index;
            }
            WriteBin(total != 0, 85 + (int)category * 4 + neighbourContext);
            if (last < 0) { return 0; }
            for (int index = 0; index < count - 1; index++)
            {
                bool significant = coefficients[index] != 0;
                WriteBin(significant, significantBase + index);
                if (!significant) { continue; }
                WriteBin(index == last, lastBase + index);
                if (index == last) { break; }
            }
            int equalOne = 0, greaterOne = 0;
            for (int index = last; index >= 0; index--)
            {
                int signed = coefficients[index];
                if (signed == 0) { continue; }
                int magnitude = Math.Abs(signed);
                int firstContext = greaterOne == 0 ? Math.Min(4, 1 + equalOne) : 0;
                WriteBin(magnitude > 1, levelBase + firstContext);
                if (magnitude > 1)
                {
                    int continuation = levelBase + 5 + Math.Min(category == H264ResidualCategory.ChromaDc ? 3 : 4, greaterOne);
                    for (int prefix = 1; prefix < 14; prefix++)
                    {
                        WriteBin(magnitude > prefix + 1, continuation);
                        if (magnitude == prefix + 1) { break; }
                    }
                    if (magnitude >= 15) { WriteExpGolomb(magnitude - 15, 0); }
                }
                writer.WriteBypass(signed < 0);
                if (magnitude == 1) { equalOne++; } else { greaterOne++; }
            }
            return total;
        }
    }
}
