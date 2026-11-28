// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static partial class H264Deblocking
    {
        private const uint StrongBoundaryMask = 0x04040404u;

        private static void ApplyProgressive420(H264Picture picture, H264SliceHeader header, int endMacroblock,
            int firstFilterable, ref H264DeblockingContext context, CancellationToken token)
        {
            ReadOnlySpan<byte> quantizers = picture.Quantizers;
            Span<H264DeblockingThresholds> thresholds = stackalloc H264DeblockingThresholds[52];
            Span<ushort> chromaQps = stackalloc ushort[52];
            H264PictureParameterSet parameters = header.Picture;
            for (int qp = 0; qp < thresholds.Length; qp++)
            {
                thresholds[qp] = Thresholds(qp, qp, header);
                int cb = picture.ChromaQuantizer.ChromaValue(qp, parameters.ChromaQpOffset);
                int cr = picture.ChromaQuantizer.ChromaValue(qp, parameters.SecondChromaQpOffset);
                chromaQps[qp] = (ushort)(cb | cr << 8);
            }

            Span<byte> yPlane = picture.Plane(0);
            Span<byte> cbPlane = picture.Plane(1);
            Span<byte> crPlane = picture.Plane(2);
            int lumaStride = picture.Stride(0);
            int cbStride = picture.Stride(1);
            int crStride = picture.Stride(2);
            int columns = header.Sequence.WidthInMbs;
            int first = header.FirstMacroblock;
            int x = first % columns * 16;
            int y = first / columns * 16;
            int width = columns * 16;
            Span<byte> strengths = stackalloc byte[32];
            bool weakKernel = Sse2.IsSupported;
            for (int address = first; address < endMacroblock; address++)
            {
                token.ThrowIfCancellationRequested();
                int qp = quantizers[address];
                int left = x == 0 ? qp : quantizers[address - 1];
                int top = address < columns ? qp : quantizers[address - columns];
                if (qp >= firstFilterable || left >= firstFilterable || top >= firstFilterable)
                {
                    int lumaStep = picture.Uses8x8Transform(address) ? 8 : 4;
                    int firstVertical = FirstFilterEdge(header, address, x, 1, lumaStep);
                    int firstHorizontal = FirstFilterEdge(header, address, y, columns, lumaStep);
                    context.BeginMacroblock(address);
                    Span<byte> vertical = strengths[..16];
                    Span<byte> horizontal = strengths[16..];
                    context.FillStrengths(x, y, true, firstVertical, lumaStep, vertical);
                    context.FillStrengths(x, y, false, firstHorizontal, lumaStep, horizontal);

                    ApplyProgressivePlane(yPlane, lumaStride, y * lumaStride + x, false, lumaStep,
                        qp, left, top, vertical, horizontal, thresholds, weakKernel);
                    int chroma = chromaQps[qp];
                    int chromaLeft = chromaQps[left];
                    int chromaTop = chromaQps[top];
                    ApplyProgressivePlane(cbPlane, cbStride, (y / 2) * cbStride + x / 2, true, 4,
                        chroma & 255, chromaLeft & 255, chromaTop & 255, vertical, horizontal, thresholds, weakKernel);
                    ApplyProgressivePlane(crPlane, crStride, (y / 2) * crStride + x / 2, true, 4,
                        chroma >> 8, chromaLeft >> 8, chromaTop >> 8, vertical, horizontal, thresholds, weakKernel);
                }
                x += 16;
                if (x == width)
                {
                    x = 0;
                    y += 16;
                }
            }
        }

        private static void ApplyProgressivePlane(Span<byte> pixels, int stride, int origin, bool chroma, int edgeStep,
            int qp, int leftQp, int topQp, ReadOnlySpan<byte> vertical, ReadOnlySpan<byte> horizontal,
            ReadOnlySpan<H264DeblockingThresholds> thresholds, bool weakKernel)
        {
            ApplyProgressiveDirection(pixels, stride, origin, true, chroma, edgeStep, vertical,
                in thresholds[(qp + leftQp + 1) >> 1], in thresholds[qp], weakKernel);
            ApplyProgressiveDirection(pixels, stride, origin, false, chroma, edgeStep, horizontal,
                in thresholds[(qp + topQp + 1) >> 1], in thresholds[qp], weakKernel);
        }

        private static void ApplyProgressiveDirection(Span<byte> pixels, int stride, int origin, bool vertical,
            bool chroma, int edgeStep, ReadOnlySpan<byte> strengths, in H264DeblockingThresholds outside,
            in H264DeblockingThresholds inside, bool weakKernel)
        {
            int step = vertical ? 1 : stride;
            int size = chroma ? 8 : 16;
            int scale = chroma ? 2 : 1;
            for (int edge = 0; edge < size; edge += edgeStep)
            {
                ReadOnlySpan<byte> strength = strengths.Slice(edge * scale, 4);
                uint groups = BinaryPrimitives.ReadUInt32LittleEndian(strength);
                if (groups == 0)
                {
                    continue;
                }
                ref readonly H264DeblockingThresholds threshold = ref (edge == 0 ? ref outside : ref inside);
                if (threshold.Alpha == 0 || threshold.Beta == 0)
                {
                    continue;
                }
                int position = origin + edge * step;
                if (weakKernel && !chroma && (groups & StrongBoundaryMask) == 0)
                {
                    H264DeblockingVector.WeakLuma16(pixels, position, stride, vertical, groups, threshold);
                }
                else
                {
                    H264DeblockingFilter.Apply(pixels, position, stride, vertical, chroma, strength, threshold);
                }
            }
        }
    }
}
