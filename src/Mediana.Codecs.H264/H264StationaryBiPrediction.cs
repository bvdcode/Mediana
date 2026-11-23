// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264StationaryBiPrediction
    {
        public static bool TryWrite(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, H264DirectMotion motion)
        {
            if ((motion.L0.X | motion.L0.Y | motion.L1.X | motion.L1.Y) != 0 || target.SampleBytes != 1 ||
                target.Structure != H264PictureStructure.Frame || target.Layout.MacroblockAdaptiveFrameField ||
                header.Picture.WeightedBiPrediction is not (0 or 2))
            {
                return false;
            }
            H264ReferencePicture? past = H264BidirectionalPrediction.Resolve(references.L0, motion.L0);
            H264ReferencePicture? future = H264BidirectionalPrediction.Resolve(references.L1, motion.L1);
            if (past is null && future is null || !SharesFrame(target, past) || !SharesFrame(target, future))
            {
                return false;
            }
            int futureWeight = 32;
            if (past is not null && future is not null && header.Picture.WeightedBiPrediction == 2)
            {
                futureWeight = H264BiPredictionWeights.Implicit(order, past, future).Weight1;
                if (futureWeight is < 0 or > 64) { return false; }
            }
            H264Picture first = (past ?? future)!.Picture;
            for (int plane = 0; plane < 3; plane++)
            {
                int horizontal = target.HorizontalScale(plane);
                int vertical = target.VerticalScale(plane);
                int stride = target.Stride(plane);
                int offset = y / vertical * stride + x / horizontal;
                ReadOnlySpan<byte> left = first.ReadPlane(plane)[offset..];
                Span<byte> destination = target.Plane(plane)[offset..];
                if (past is null || future is null)
                {
                    H264BlockRows.Copy(left, stride, destination, stride, 16 / horizontal, 16 / vertical);
                }
                else
                {
                    BlendRows(left, future.Picture.ReadPlane(plane)[offset..], destination, stride, 16 / horizontal, 16 / vertical, futureWeight);
                }
            }
            target.MarkStationarySkip(x, y, 1, motion.L0.ReferenceIndex, motion.L1.ReferenceIndex);
            return true;
        }

        private static bool SharesFrame(H264Picture target, H264ReferencePicture? reference) =>
            reference is null || (reference.Companion is null && reference.Structure == H264PictureStructure.Frame &&
                reference.Picture.Structure == H264PictureStructure.Frame && ReferenceEquals(reference.Picture.Sequence, target.Sequence));

        internal static void BlendRows(ReadOnlySpan<byte> past, ReadOnlySpan<byte> future, Span<byte> output,
            int stride, int width, int height, int futureWeight)
        {
            Vector128<ushort> w0 = Vector128.Create((ushort)(64 - futureWeight));
            Vector128<ushort> w1 = Vector128.Create((ushort)futureWeight);
            for (int row = 0; row < height; row++)
            {
                int start = row * stride;
                if (!Vector128.IsHardwareAccelerated)
                {
                    for (int index = start; index < start + width; index++)
                    {
                        output[index] = (byte)((past[index] * (64 - futureWeight) + future[index] * futureWeight + 32) >> 6);
                    }
                }
                else if (width == 16)
                {
                    Vector128<byte> a = MemoryMarshal.Read<Vector128<byte>>(past[start..]);
                    Vector128<byte> b = MemoryMarshal.Read<Vector128<byte>>(future[start..]);
                    Vector128<byte> result = futureWeight == 32 ? H264SampleAverage.Rounded(a, b) :
                        Vector128.Narrow(Blend(Vector128.WidenLower(a), Vector128.WidenLower(b), w0, w1),
                            Blend(Vector128.WidenUpper(a), Vector128.WidenUpper(b), w0, w1));
                    MemoryMarshal.Write(output[start..], in result);
                }
                else
                {
                    Vector128<byte> a = Vector128.CreateScalar(MemoryMarshal.Read<ulong>(past[start..])).AsByte();
                    Vector128<byte> b = Vector128.CreateScalar(MemoryMarshal.Read<ulong>(future[start..])).AsByte();
                    Vector128<byte> result = futureWeight == 32 ? H264SampleAverage.Rounded(a, b) :
                        Vector128.Narrow(Blend(Vector128.WidenLower(a), Vector128.WidenLower(b), w0, w1), Vector128<ushort>.Zero);
                    ulong packed = result.AsUInt64().GetElement(0);
                    MemoryMarshal.Write(output[start..], in packed);
                }
            }
        }

        private static Vector128<ushort> Blend(Vector128<ushort> a, Vector128<ushort> b, Vector128<ushort> w0, Vector128<ushort> w1) =>
            (a * w0 + b * w1 + Vector128.Create((ushort)32)) >> 6;
    }
}
