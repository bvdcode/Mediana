// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Hevc
{
    /// <summary>Reuses one bounded worker buffer for interpolation, weighting and picture-plane writes.</summary>
    internal class HevcMotionCompensator : IDisposable
    {
        private const int MaximumBlockSize = 64;
        private const int PredictionSamples = MaximumBlockSize * MaximumBlockSize;
        private readonly WritableMediaBuffer storage;
        private bool disposed;

        public HevcMotionCompensator(MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            int working = HevcInterpolation.GetWorkingSampleCount(MaximumBlockSize, MaximumBlockSize, false);
            storage = allocator.Allocate(checked((2 * PredictionSamples + working) * sizeof(int)));
        }

        public void Predict(DecodedPicture target, int x, int y, int width, int height,
            HevcPredictionReference? first, HevcPredictionReference? second, HevcPredictionWeights? weights)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(target);
            if (!first.HasValue && !second.HasValue) { throw new InvalidDataException("An HEVC prediction requires a reference list."); }
            SequenceParameters sequence = target.Sequence;
            if (x < 0 || y < 0 || width < 4 || height < 4 || width > MaximumBlockSize || height > MaximumBlockSize ||
                x > sequence.Width - width || y > sequence.Height - height || ((x | y | width | height) & 3) != 0)
            { throw new ArgumentOutOfRangeException(nameof(width)); }
            ValidateReference(first, target); ValidateReference(second, target);
            int componentCount = sequence.ChromaFormat == 0 ? 1 : 3;
            Span<int> buffers = MemoryMarshal.Cast<byte, int>(storage.Memory.Span);
            Span<int> working = buffers[(2 * PredictionSamples)..];
            for (int component = 0; component < componentCount; component++)
            {
                int scaleX = component == 0 ? 1 : sequence.SubWidthC, scaleY = component == 0 ? 1 : sequence.SubHeightC;
                int blockWidth = width / scaleX, blockHeight = height / scaleY, count = blockWidth * blockHeight;
                Span<int> prediction0 = buffers[..count], prediction1 = buffers.Slice(PredictionSamples, count);
                if (first is HevcPredictionReference reference0)
                { Interpolate(reference0, x / scaleX, y / scaleY, blockWidth, blockHeight, component, prediction0, working); }
                if (second is HevcPredictionReference reference1)
                { Interpolate(reference1, x / scaleX, y / scaleY, blockWidth, blockHeight, component, prediction1, working); }
                int denominator = 0;
                HevcPredictionWeight weight0 = new(1, 0, 1, 0, 1, 0), weight1 = weight0;
                if (weights is not null)
                {
                    denominator = component == 0 ? weights.LumaDenominator : weights.ChromaDenominator;
                    if (first.HasValue) { weight0 = weights.List0[first.Value.Index]; }
                    if (second.HasValue) { weight1 = weights.List1[second.Value.Index]; }
                }
                (int multiplier0, int offset0) = ComponentWeight(weight0, component);
                (int multiplier1, int offset1) = ComponentWeight(weight1, component);
                if (first.HasValue && second.HasValue)
                {
                    HevcWeightedPrediction.Bidirectional(prediction0, prediction1, prediction0, sequence.BitDepth,
                        denominator, multiplier0, multiplier1, offset0, offset1);
                }
                else if (first.HasValue)
                { HevcWeightedPrediction.Unidirectional(prediction0, prediction0, sequence.BitDepth, denominator, multiplier0, offset0); }
                else
                { HevcWeightedPrediction.Unidirectional(prediction1, prediction0, sequence.BitDepth, denominator, multiplier1, offset1); }
                SamplePlane output = Plane(target, component);
                switch (sequence.BitDepth)
                {
                    case 8: Store<byte, ByteSampleFormat>(output.ByteView, x / scaleX, y / scaleY, blockWidth, blockHeight, prediction0); break;
                    case 10: Store<ushort, WordSampleFormat>(output.WordView, x / scaleX, y / scaleY, blockWidth, blockHeight, prediction0); break;
                    default: throw new InvalidOperationException("Unsupported HEVC prediction precision.");
                }
            }
        }

        private static void ValidateReference(HevcPredictionReference? reference, DecodedPicture target)
        {
            if (reference is not HevcPredictionReference value) { return; }
            if (value.Picture is null || ReferenceEquals(value.Picture, target))
            { throw new InvalidDataException("HEVC motion compensation requires a different reconstructed reference picture."); }
            SequenceParameters a = value.Picture.Sequence, b = target.Sequence;
            if (a.Width != b.Width || a.Height != b.Height || a.BitDepth != b.BitDepth || a.ChromaFormat != b.ChromaFormat)
            { throw new InvalidDataException("HEVC motion references have incompatible picture geometry."); }
            ArgumentOutOfRangeException.ThrowIfNegative(value.Index);
        }

        private static void Interpolate(HevcPredictionReference reference, int x, int y, int width, int height,
            int component, Span<int> output, Span<int> working)
        {
            SequenceParameters sequence = reference.Picture.Sequence;
            bool chroma = component != 0;
            int fractionBitsX = 2, fractionBitsY = 2;
            if (chroma)
            {
                fractionBitsX += sequence.SubWidthC - 1;
                fractionBitsY += sequence.SubHeightC - 1;
            }
            int sourceX = x + (reference.Motion.X >> fractionBitsX), sourceY = y + (reference.Motion.Y >> fractionBitsY);
            int fractionX = reference.Motion.X & ((1 << fractionBitsX) - 1), fractionY = reference.Motion.Y & ((1 << fractionBitsY) - 1);
            if (chroma) { fractionX <<= 3 - fractionBitsX; fractionY <<= 3 - fractionBitsY; }
            SamplePlane source = Plane(reference.Picture, component);
            switch (sequence.BitDepth)
            {
                case 8:
                    HevcInterpolation.Predict<byte, ByteSampleFormat>(source.ReadByteView, sourceX, sourceY, fractionX, fractionY,
                    chroma, 8, width, height, output, working); break;
                case 10:
                    HevcInterpolation.Predict<ushort, WordSampleFormat>(source.ReadWordView, sourceX, sourceY, fractionX, fractionY,
                    chroma, 10, width, height, output, working); break;
                default: throw new InvalidOperationException("Unsupported HEVC reference precision.");
            }
        }

        private static (int Weight, int Offset) ComponentWeight(HevcPredictionWeight weight, int component) => component switch
        {
            0 => (weight.LumaWeight, weight.LumaOffset),
            1 => (weight.CbWeight, weight.CbOffset),
            2 => (weight.CrWeight, weight.CrOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

        private static SamplePlane Plane(DecodedPicture picture, int component) => component switch
        {
            0 => picture.Luma,
            1 => picture.Cb ?? throw new InvalidDataException("HEVC reference has no Cb component."),
            2 => picture.Cr ?? throw new InvalidDataException("HEVC reference has no Cr component."),
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

        private static void Store<TSample, TFormat>(SamplePlaneView<TSample> plane, int x, int y, int width, int height, ReadOnlySpan<int> prediction)
            where TSample : unmanaged where TFormat : struct, ISampleFormat<TSample>
        {
            for (int row = 0; row < height; row++)
            {
                Span<TSample> output = plane.Row(y + row).Slice(x, width);
                ReadOnlySpan<int> input = prediction.Slice(row * width, width);
                for (int column = 0; column < width; column++) { output[column] = default(TFormat).Write(input[column]); }
            }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            storage.Dispose(); disposed = true;
        }
    }
}
