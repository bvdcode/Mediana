// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Runtime.CompilerServices;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Reads quantized transform coefficients in reverse grouped scan order (HEVC 7.3.8.11).
    /// </summary>
    /// <remarks>
    /// Output belongs to the caller and is cleared before use. Coordinates map to
    /// coefficients[y * (1 &lt;&lt; log2Size) + x]. Quantized levels are signed 16-bit values;
    /// malformed magnitudes are rejected before dequantization. Contexts remain slice-local.
    /// </remarks>
    internal class ResidualDecoder(IntraCabacReader reader, bool transformSkipEnabled, bool signDataHiding, bool fullResolutionChroma = false)
    {
        private const uint InitialGroupLevels = 0x55555555;

        public bool Read(Span<int> coefficients, int log2Size, int component, int intraMode, bool bypass)
        {
            int size = 1 << log2Size;
            Span<int> output = coefficients.Slice(0, size * size);
            output.Clear();
            bool skip = transformSkipEnabled && !bypass && (log2Size == 2) && reader.Read(CabacElement.TransformSkip, component == 0 ? 0 : 1);
            int scanIndex = ScanIndex(log2Size, component, intraMode);
            int prefixX = LastPrefix(log2Size, component, CabacElement.LastX);
            int prefixY = LastPrefix(log2Size, component, CabacElement.LastY);
            int lastX = LastPosition(prefixX);
            int lastY = LastPosition(prefixY);
            if (scanIndex == 2)
            {
                (lastX, lastY) = (lastY, lastX);
            }
            int lastOffset = (lastY * size) + lastX;
            ReadOnlySpan<int> scan = CoefficientScan.Block(log2Size, scanIndex);
            int last = scan.IndexOf(lastOffset);
            if (last < 0)
            {
                throw new InvalidDataException("Last coefficient exceeds the transform block.");
            }
            Span<byte> codedGroups = stackalloc byte[64];
            codedGroups.Clear();
            int groupWidth = size / 4;
            int previousGreater = 1;
            Span<int> positions = stackalloc int[16];
            for (int group = last / 16; group >= 0; group--)
            {
                int firstOffset = scan[group * 16];
                int gx = (firstOffset % size) / 4;
                int gy = firstOffset / size / 4;
                bool coded = (group == 0) || (group == (last / 16)) || reader.Read(CabacElement.CodedSubBlock,
                    CoefficientContexts.Group(gx, gy, groupWidth, component, codedGroups));
                codedGroups[(gy * groupWidth) + gx] = coded ? (byte)1 : (byte)0;
                if (!coded)
                {
                    continue;
                }
                ReadOnlySpan<byte> contexts = CoefficientContexts.SignificantGroup(gx, gy, log2Size, scanIndex, component, codedGroups);
                int count = ReadSignificantPositions(positions, contexts, group, last);

                if (count != 0)
                {
                    ReadLevels(output, scan.Slice(group * 16, 16), positions.Slice(0, count), group, component, bypass, ref previousGreater);
                }
            }

            return skip;
        }

        private void ReadLevels(Span<int> output, ReadOnlySpan<int> scan, ReadOnlySpan<int> positions,
            int group, int component, bool bypass, ref int previousGreater)
        {
            uint levels = ReadGreaterFlags(positions.Length, group, component, ref previousGreater, out int firstGreater);

            bool hidden = signDataHiding && !bypass && ((positions[0] - positions[positions.Length - 1]) > 3);
            int signCount = positions.Length - (hidden ? 1 : 0);
            int signs = reader.Bits(signCount) << (32 - signCount);
            int rice = 0;
            int previousAbsolute = 0;
            int sum = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                int level = (int)(levels & 3);
                levels >>= 2;
                int threshold = i < 8 ? (i == firstGreater ? 3 : 2) : 1;
                if (level == threshold)
                {
                    if (previousAbsolute > (3 << rice))
                    {
                        rice = Math.Min(4, rice + 1);
                    }
                    level += reader.RemainingLevel(rice);
                    previousAbsolute = level;
                }

                sum += level;
                bool negative = signs < 0;
                signs <<= 1;
                if (hidden && (i == (positions.Length - 1)) && ((sum & 1) != 0))
                {
                    negative = !negative;
                }
                int signed = negative ? -level : level;
                if ((signed < short.MinValue) || (signed > short.MaxValue))
                {
                    throw new InvalidDataException("Transform coefficient exceeds signed 16-bit precision.");
                }
                output[scan[positions[i]]] = signed;
            }
        }

        private int LastPrefix(int log2Size, int component, CabacElement element)
        {
            int offset = component == 0 ? (3 * (log2Size - 2)) + ((log2Size - 1) >> 2) : 15;
            int shift = component == 0 ? (log2Size + 1) >> 2 : log2Size - 2;
            int prefix = 0;
            while ((prefix < ((2 * log2Size) - 1)) && reader.Read(element, offset + (prefix >> shift)))
            {
                prefix++;
            }
            return prefix;
        }

        private int LastPosition(int prefix)
        {
            if (prefix < 4)
            {
                return prefix;
            }
            int suffixLength = (prefix >> 1) - 1;
            return ((2 + (prefix & 1)) << suffixLength) + reader.Bits(suffixLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ScanIndex(int log2Size, int component, int intraMode)
        {
            int scanIndex = 0;
            if ((log2Size == 2) || ((log2Size == 3) && ((component == 0) || fullResolutionChroma)))
            {
                if ((intraMode >= 6) && (intraMode <= 14))
                {
                    scanIndex = 2;
                }
                else if ((intraMode >= 22) && (intraMode <= 30))
                {
                    scanIndex = 1;
                }
            }
            return scanIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadSignificantPositions(Span<int> positions, ReadOnlySpan<byte> contexts, int group, int last)
        {
            int count = 0;
            bool inferDc = (group != 0) && (group != (last / 16));
            int upper = group == (last / 16) ? last % 16 : 15;
            for (int n = upper; n >= 0; n--)
            {
                bool significant = (((group * 16) + n) == last) || ((n == 0) && inferDc) ||
                    reader.Read(CabacElement.SignificantCoefficient, contexts[n]);
                if (significant)
                {
                    positions[count++] = n;
                    inferDc = false;
                }
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadGreaterFlags(int count, int group, int component, ref int previousGreater, out int firstGreater)
        {
            uint levels = InitialGroupLevels;
            int contextSet = (component == 0) && (group != 0) ? 2 : 0;
            if (previousGreater == 0)
            {
                contextSet++;
            }
            int greaterContext = 1;
            firstGreater = -1;
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                int context = (contextSet * 4) + Math.Min(3, greaterContext) + (component == 0 ? 0 : 16);
                bool greater = reader.Read(CabacElement.GreaterOne, context);
                if (greater)
                {
                    levels += 1u << (2 * i);
                    if (firstGreater == -1)
                    {
                        firstGreater = i;
                    }
                    greaterContext = 0;
                }
                else if (greaterContext > 0)
                {
                    greaterContext++;
                }
            }

            previousGreater = greaterContext;
            if ((firstGreater >= 0) && reader.Read(CabacElement.GreaterTwo, contextSet + (component == 0 ? 0 : 4)))
            {
                levels += 1u << (2 * firstGreater);
            }
            return levels;
        }
    }
}
