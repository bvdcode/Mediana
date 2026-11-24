// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal ref partial struct H264DeblockingContext
    {
        public void FillStrengths(int x, int y, bool vertical, int firstEdge, int edgeStep, scoped Span<byte> strengths)
        {
            if (strengths.Length != 16 || firstEdge is < 0 or > 16 || (firstEdge & 3) != 0
                || edgeStep is not (4 or 8 or 16))
            {
                throw new ArgumentException("Invalid H.264 deblocking edge range.");
            }
            uniformPrediction = null;
            uniformValidated = false;
            strengths.Clear();
            int endEdge = 16;
            if (flags[address].HasFlag(H264MacroblockFlags.StationarySkip))
            {
                // The interior of a stationary skipped block shares one prediction and has no coded residuals.
                endEdge = edgeStep;
                if (firstEdge == 0 && flags[address - (vertical ? 1 : columns)].HasFlag(H264MacroblockFlags.StationarySkip))
                {
                    strengths[..4].Fill((byte)Strength(x, y, vertical, macroblockEdge: true));
                    return;
                }
            }
            int firstInterior = Math.Max(firstEdge, edgeStep);
            for (int edge = firstEdge; edge < endEdge; edge += edgeStep)
            {
                if (edge == firstInterior)
                {
                    int first = y / 4 * stride + x / 4;
                    if (HasOnlyPredictedSamples(first) && UniformPrediction(first, vertical))
                    {
                        break;
                    }
                }
                for (int segment = 0; segment < 4; segment++)
                {
                    int sampleX = x + (vertical ? edge : segment * 4);
                    int sampleY = y + (vertical ? segment * 4 : edge);
                    strengths[edge + segment] = (byte)Strength(sampleX, sampleY, vertical, edge == 0, true);
                }
            }
        }

        private readonly bool HasOnlyPredictedSamples(int first)
        {
            const uint interModes = H264Picture.InterMode * 0x01010101u;
            for (int row = 0; row < 4; row++)
            {
                int start = first + row * stride;
                if (MemoryMarshal.Read<uint>(modes[start..]) != interModes || MemoryMarshal.Read<uint>(counts[start..]) != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private bool UniformPrediction(int position, bool vertical)
        {
            uniformPrediction ??= IsUniformPrediction();
            if (uniformPrediction != true)
            {
                return false;
            }
            if (!uniformValidated)
            {
                H264PictureIdentity? l0 = Resolve(H264ReferenceList.L0, position, false, vertical, false);
                H264PictureIdentity? l1 = picture.HasBidirectionalMotion
                    ? Resolve(H264ReferenceList.L1, position, false, vertical, false) : null;
                if (l0 is null && l1 is null)
                {
                    throw new InvalidDataException("The H.264 inter edge has no prediction reference.");
                }
                uniformValidated = true;
            }
            return true;
        }

        private readonly bool IsUniformPrediction()
        {
            int first = address / columns * 4 * stride + address % columns * 4;
            return IsUniform(indicesL0, motionXL0, motionYL0, first)
                && (!picture.HasBidirectionalMotion || IsUniform(indicesL1, motionXL1, motionYL1, first));
        }

        private readonly bool IsUniform(ReadOnlySpan<byte> indices, ReadOnlySpan<int> motionX, ReadOnlySpan<int> motionY, int first)
        {
            byte reference = indices[first];
            uint repeated = reference * 0x01010101u;
            Vector128<int> x = Vector128.Create(motionX[first]);
            Vector128<int> y = Vector128.Create(motionY[first]);
            for (int row = 0; row < 4; row++)
            {
                int start = first + row * stride;
                if (MemoryMarshal.Read<uint>(indices.Slice(start, 4)) != repeated)
                {
                    return false;
                }
                if (reference != byte.MaxValue
                    && (!Vector128.EqualsAll(Vector128.Create(motionX.Slice(start, 4)), x)
                        || !Vector128.EqualsAll(Vector128.Create(motionY.Slice(start, 4)), y)))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
