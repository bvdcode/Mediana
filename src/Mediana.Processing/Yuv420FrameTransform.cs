// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Numerics;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Reusable orthogonal YUV420 transform with explicitly selected integer sample precision.
    /// Converts chroma sampling and applies orientation directly in planar storage, inverts SAR when axes swap and retains colorimetry.
    /// Oriented chroma is bilinearly resited to center; precision reduction rounds and saturates stored samples,
    /// without tone mapping or changing transfer functions. Borrows input and allocator; returns an owned frame.</summary>
    public class Yuv420FrameTransform
    {
        private readonly VideoFormat inputFormat;
        private readonly OrthogonalTransform orientation;
        private readonly MediaBufferAllocator allocator;
        private readonly int inputDepth, outputDepth, inputSubsamplingX, inputSubsamplingY;
        private readonly bool resampleChroma;
        private readonly int inputBytes;
        private readonly int outputBytes;
        private readonly int horizontalPhase;
        private readonly int verticalPhase;

        public Yuv420FrameTransform(VideoFormat inputFormat, VideoDisplayMatrix sourceDisplayMatrix,
            VideoPixelFormat outputPixelFormat, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(outputPixelFormat);
            ArgumentNullException.ThrowIfNull(allocator);
            inputDepth = SamplePrecision(inputFormat.PixelFormat); outputDepth = SamplePrecision(outputPixelFormat);
            PlanarVideoPixelFormat inputPixels = (PlanarVideoPixelFormat)inputFormat.PixelFormat, outputPixels = (PlanarVideoPixelFormat)outputPixelFormat;
            if (outputPixels.HorizontalSubsampling != 2 || outputPixels.VerticalSubsampling != 2) { throw new NotSupportedException("YUV420 transform output requires 4:2:0 subsampling."); }
            inputSubsamplingX = inputPixels.HorizontalSubsampling; inputSubsamplingY = inputPixels.VerticalSubsampling;
            resampleChroma = inputSubsamplingX != 2 || inputSubsamplingY != 2;
            inputBytes = inputDepth == 8 ? 1 : 2; outputBytes = outputDepth == 8 ? 1 : 2;
            if (outputDepth > inputDepth)
            {
                throw new NotSupportedException("YUV precision expansion must be performed explicitly by a precision-expansion converter.");
            }
            this.inputFormat = inputFormat;
            this.allocator = allocator;
            orientation = OrthogonalTransform.FromMatrix(sourceDisplayMatrix);
            VideoColorInfo color = inputFormat.Color;
            if (orientation != OrthogonalTransform.Identity || resampleChroma)
            {
                if (inputSubsamplingX != 1 || inputSubsamplingY != 1)
                { (horizontalPhase, verticalPhase) = ChromaSamplePosition.InHalfLumaSamples(color.ChromaLocation); }
                if (inputSubsamplingX == 1) { horizontalPhase = 0; }
                if (inputSubsamplingY == 1) { verticalPhase = 0; }
                color = color with { ChromaLocation = ChromaLocation.Center };
            }
            SampleAspectRatio? aspect = inputFormat.SampleAspectRatio;
            if (orientation.SwapsAxes && aspect is SampleAspectRatio known)
            {
                aspect = new(known.Denominator, known.Numerator);
            }
            OutputFormat = new(orientation.SwapsAxes ? inputFormat.Height : inputFormat.Width,
                orientation.SwapsAxes ? inputFormat.Width : inputFormat.Height,
                outputPixelFormat, color, aspect, inputFormat.ColorDescription);
        }

        public VideoFormat OutputFormat { get; }

        /// <summary>Retains unchanged input or performs orientation and precision conversion in one allocation.
        /// Calls may run concurrently when the borrowed inputs and allocator permit concurrent access.</summary>
        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateFormat(input.Format);
            if (orientation == OrthogonalTransform.Identity && inputDepth == outputDepth && !resampleChroma) { return input.WithFormat(OutputFormat); }
            PlaneLayout[] planes = OutputFormat.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            for (int plane = 0; plane < planes.Length; plane++)
            {
                PlaneLayout target = planes[plane];
                for (int y = 0; y < target.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<byte> output = writable.Memory.Span.Slice(target.Offset + y * target.Stride, target.RowBytes);
                    if (orientation == OrthogonalTransform.Identity && (plane == 0 || !resampleChroma))
                    {
                        if (inputDepth == outputDepth) { input.GetRow(plane, y).CopyTo(output); }
                        else { ReduceRow(input.GetRow(plane, y), output, cancellationToken); }
                        continue;
                    }
                    for (int x = 0; x < target.RowBytes / outputBytes; x++)
                    {
                        if ((x & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                        int value;
                        int fractionalBits;
                        if (plane == 0)
                        {
                            int sourceX = orientation.SourceX(x, y, inputFormat.Width);
                            int sourceY = orientation.SourceY(x, y, inputFormat.Height);
                            value = ReadSample(input.GetRow(plane, sourceY), sourceX);
                            fractionalBits = 0;
                        }
                        else
                        {
                            long lumaX = 4L * x + 1;
                            long lumaY = 4L * y + 1;
                            long sourceX = orientation.A * lumaX + orientation.B * lumaY
                                + (orientation.A + orientation.B < 0 ? 2L * (inputFormat.Width - 1) : 0) - horizontalPhase;
                            long sourceY = orientation.C * lumaX + orientation.D * lumaY
                                + (orientation.C + orientation.D < 0 ? 2L * (inputFormat.Height - 1) : 0) - verticalPhase;
                            value = Interpolate(input, plane, sourceX, sourceY);
                            fractionalBits = 4;
                        }
                        int shift = fractionalBits + inputDepth - outputDepth;
                        if (shift != 0) { value = (value + (1 << (shift - 1))) >> shift; }
                        if (outputBytes == 1) { output[x] = (byte)Math.Min(255, value); }
                        else { MemoryMarshal.Cast<byte, ushort>(output)[x] = (ushort)value; }
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new(OutputFormat, buffer, planes, input.Timestamp, input.Duration);
        }

        private int Interpolate(VideoFrame input, int plane, long x, long y)
        {
            x *= 2 / inputSubsamplingX; y *= 2 / inputSubsamplingY;
            int width = input.Planes[plane].RowBytes / inputBytes;
            int height = input.Planes[plane].Height;
            int left = (int)Math.Clamp(x >> 2, 0, width - 1);
            int right = (int)Math.Clamp((x >> 2) + 1, 0, width - 1);
            int top = (int)Math.Clamp(y >> 2, 0, height - 1);
            int bottom = (int)Math.Clamp((y >> 2) + 1, 0, height - 1);
            int fractionX = (int)(x & 3);
            int fractionY = (int)(y & 3);
            ReadOnlySpan<byte> upper = input.GetRow(plane, top);
            ReadOnlySpan<byte> lower = input.GetRow(plane, bottom);
            int a = ReadSample(upper, left) * (4 - fractionX) + ReadSample(upper, right) * fractionX;
            int b = ReadSample(lower, left) * (4 - fractionX) + ReadSample(lower, right) * fractionX;
            return a * (4 - fractionY) + b * fractionY;
        }

        private void ReduceRow(ReadOnlySpan<byte> input, Span<byte> output, CancellationToken token)
        {
            ReadOnlySpan<ushort> samples = MemoryMarshal.Cast<byte, ushort>(input);
            int x = 0, shift = inputDepth - outputDepth, maximum = (1 << inputDepth) - 1, saturation = (1 << outputDepth) - 1;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<ushort> max = new((ushort)maximum), limit = new((ushort)saturation), one = new(1);
                int count = outputBytes == 1 ? Vector<byte>.Count : Vector<ushort>.Count;
                for (; x <= samples.Length - count; x += count)
                {
                    if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                    Vector<ushort> lower = new(samples.Slice(x, Vector<ushort>.Count));
                    if (Vector.GreaterThanAny(lower, max)) { throw new InvalidDataException("A planar YUV sample exceeds its declared precision."); }
                    lower = Vector.Min((lower >> shift) + ((lower >> (shift - 1)) & one), limit);
                    if (outputBytes == 2) { lower.CopyTo(MemoryMarshal.Cast<byte, ushort>(output).Slice(x)); continue; }
                    Vector<ushort> upper = new(samples.Slice(x + Vector<ushort>.Count, Vector<ushort>.Count));
                    if (Vector.GreaterThanAny(upper, max)) { throw new InvalidDataException("A planar YUV sample exceeds its declared precision."); }
                    upper = Vector.Min((upper >> shift) + ((upper >> (shift - 1)) & one), limit);
                    Vector.Narrow(lower, upper).CopyTo(output.Slice(x, count));
                }
            }
            for (; x < samples.Length; x++)
            {
                if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                int value = samples[x];
                if (value > maximum) { throw new InvalidDataException("A planar YUV sample exceeds its declared precision."); }
                value = Math.Min(saturation, (value + (1 << (shift - 1))) >> shift);
                if (outputBytes == 1) { output[x] = (byte)value; } else { MemoryMarshal.Cast<byte, ushort>(output)[x] = (ushort)value; }
            }
        }

        private int ReadSample(ReadOnlySpan<byte> row, int x)
        {
            if (inputBytes == 1) { return row[x]; }
            int value = MemoryMarshal.Cast<byte, ushort>(row)[x];
            if (value >= (1 << inputDepth)) { throw new InvalidDataException("A planar YUV sample exceeds its declared precision."); }
            return value;
        }

        private void ValidateFormat(VideoFormat format)
        {
            bool sameColor = format.Color == inputFormat.Color && Equals(format.ColorDescription, inputFormat.ColorDescription);
            if (format.Width != inputFormat.Width || format.Height != inputFormat.Height ||
                format.PixelFormat != inputFormat.PixelFormat || format.SampleAspectRatio != inputFormat.SampleAspectRatio ||
                (!sameColor && format.GetColorInterpretation() != inputFormat.GetColorInterpretation()))
            {
                throw new NotSupportedException("The input frame differs from the configured YUV transform.");
            }
        }

        private static int SamplePrecision(VideoPixelFormat format)
        {
            if (format is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Yuv } planar)
            { return planar.BitDepth; }
            throw new NotSupportedException("YUV420 transforms require planar integer YUV input.");
        }
    }
}
