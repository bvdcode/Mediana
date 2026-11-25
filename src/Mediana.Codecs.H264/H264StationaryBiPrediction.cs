// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264StationaryBiPrediction
    {
        public static bool TryWrite(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, H264DirectMotion motion)
        {
            if (!TrySelect(target, header, references, order, motion, out H264Picture? first,
                out H264Picture? second, out int futureWeight))
            {
                return false;
            }
            WriteStrip(target, x, y, 1, first, second, futureWeight);
            target.MarkStationarySkip(x, y, 1, motion.L0.ReferenceIndex, motion.L1.ReferenceIndex);
            return true;
        }

        internal static bool TrySelect(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, H264DirectMotion motion, [NotNullWhen(true)] out H264Picture? first,
            out H264Picture? second, out int futureWeight)
        {
            first = null;
            second = null;
            futureWeight = 32;
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
            if (past is not null && future is not null && header.Picture.WeightedBiPrediction == 2)
            {
                futureWeight = H264BiPredictionWeights.Implicit(order, past, future).Weight1;
                if (futureWeight is < 0 or > 64)
                {
                    return false;
                }
            }
            first = (past ?? future)!.Picture;
            second = past is null ? null : future?.Picture;
            return true;
        }

        internal static void WriteStrip(H264Picture target, int x, int y, int blocks, H264Picture first,
            H264Picture? second, int futureWeight)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int horizontal = target.HorizontalScale(plane);
                int vertical = target.VerticalScale(plane);
                int stride = target.Stride(plane);
                int offset = y / vertical * stride + x / horizontal;
                ReadOnlySpan<byte> left = first.ReadPlane(plane)[offset..];
                Span<byte> destination = target.Plane(plane)[offset..];
                if (second is null)
                {
                    H264BlockRows.Copy(left, stride, destination, stride, 16 / horizontal * blocks, 16 / vertical);
                }
                else
                {
                    BlendRows(left, second.ReadPlane(plane)[offset..], destination, stride, 16 / horizontal * blocks, 16 / vertical, futureWeight);
                }
            }
        }

        private static bool SharesFrame(H264Picture target, H264ReferencePicture? reference) =>
            reference is null || (reference.Companion is null && reference.Structure == H264PictureStructure.Frame &&
                reference.Picture.Structure == H264PictureStructure.Frame && ReferenceEquals(reference.Picture.Sequence, target.Sequence));

        internal static void BlendRows(ReadOnlySpan<byte> past, ReadOnlySpan<byte> future, Span<byte> output,
            int stride, int width, int height, int futureWeight)
        {
            if (futureWeight == 32 && Vector128.IsHardwareAccelerated)
            {
                AverageRows(past, future, output, stride, width, height);
                return;
            }
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
                else
                {
                    int index = start;
                    int end = start + width;
                    for (; index <= end - 16; index += 16)
                    {
                        Vector128<byte> a = MemoryMarshal.Read<Vector128<byte>>(past[index..]);
                        Vector128<byte> b = MemoryMarshal.Read<Vector128<byte>>(future[index..]);
                        Vector128<byte> result = Vector128.Narrow(Blend(Vector128.WidenLower(a), Vector128.WidenLower(b), w0, w1),
                                Blend(Vector128.WidenUpper(a), Vector128.WidenUpper(b), w0, w1));
                        MemoryMarshal.Write(output[index..], in result);
                    }
                    if (index < end)
                    {
                        Vector128<byte> a = Vector128.CreateScalar(MemoryMarshal.Read<ulong>(past[index..])).AsByte();
                        Vector128<byte> b = Vector128.CreateScalar(MemoryMarshal.Read<ulong>(future[index..])).AsByte();
                        Vector128<byte> result = Vector128.Narrow(Blend(Vector128.WidenLower(a), Vector128.WidenLower(b), w0, w1), Vector128<ushort>.Zero);
                        ulong packed = result.AsUInt64().GetElement(0);
                        MemoryMarshal.Write(output[index..], in packed);
                    }
                }
            }
        }

        private static void AverageRows(ReadOnlySpan<byte> past, ReadOnlySpan<byte> future, Span<byte> output,
            int stride, int width, int height)
        {
            int required = checked((height - 1) * stride + width);
            ref byte first = ref MemoryMarshal.GetReference(past[..required]);
            ref byte second = ref MemoryMarshal.GetReference(future[..required]);
            ref byte destination = ref MemoryMarshal.GetReference(output[..required]);
            for (int y = 0; y < height; y++)
            {
                nuint index = (nuint)(y * stride);
                nuint end = index + (nuint)width;
                for (; end - index >= 16; index += 16)
                {
                    H264SampleAverage.Rounded(Vector128.LoadUnsafe(ref first, index), Vector128.LoadUnsafe(ref second, index))
                        .StoreUnsafe(ref destination, index);
                }
                if (index < end)
                {
                    Vector128<byte> a = Vector64.LoadUnsafe(ref first, index).ToVector128Unsafe();
                    Vector128<byte> b = Vector64.LoadUnsafe(ref second, index).ToVector128Unsafe();
                    H264SampleAverage.Rounded(a, b).GetLower().StoreUnsafe(ref destination, index);
                }
            }
        }

        private static Vector128<ushort> Blend(Vector128<ushort> a, Vector128<ushort> b, Vector128<ushort> w0, Vector128<ushort> w1) =>
            (a * w0 + b * w1 + Vector128.Create((ushort)32)) >> 6;
    }
}
