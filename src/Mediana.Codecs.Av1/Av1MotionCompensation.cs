// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1MotionCompensation
    {
        internal static void Apply(Span<byte> output, Av1SequenceHeader target, Av1StoredPicture reference,
            int plane, int x, int y, int width, int height, Av1MotionBlock motion, int predictionWidth, int predictionHeight)
            => Convolve(new(output[target.PlaneOffset(plane)..], target.PlaneStride(plane)), target, reference, plane, x, y, width, height, motion, default, predictionWidth, predictionHeight);

        internal static void Predict(Span<byte> output, Av1SequenceHeader target, Av1StoredPicture reference,
            int plane, int x, int y, int width, int height, Av1MotionBlock motion)
            => Convolve(new(output, width, x, y), target, reference, plane, x, y, width, height, motion, default, width, height);

        internal static void ApplyCompound(Span<byte> output, Av1SequenceHeader target, Av1StoredPicture first, Av1StoredPicture second,
            int plane, int x, int y, int width, int height, Av1MotionBlock motion, Av1CompoundPrediction compound, int predictionWidth, int predictionHeight,
            Span<byte> mask = default, int maskStride = 0, Av1WarpParameters firstWarp = default, Av1WarpParameters secondWarp = default)
        {
            Span<short> prediction = stackalloc short[width * height];
            Av1PredictionDestination destination = new(output[target.PlaneOffset(plane)..], target.PlaneStride(plane));
            Convolve(destination, target, first, plane, x, y, width, height, motion, new(prediction), predictionWidth, predictionHeight, firstWarp);
            motion.Row = motion.SecondRow;
            motion.Column = motion.SecondColumn;
            Convolve(destination, target, second, plane, x, y, width, height, motion, new(prediction, compound, mask, maskStride, target.PlaneShift(plane)), predictionWidth, predictionHeight, secondWarp);
        }

        private static void Convolve(Av1PredictionDestination destination, Av1SequenceHeader target, Av1StoredPicture reference,
            int plane, int x, int y, int width, int height, Av1MotionBlock motion, Av1CompoundBlend blend, int predictionWidth, int predictionHeight, Av1WarpParameters warp = default)
        {
            if (warp.IsValid)
            {
                Av1WarpPrediction.Predict(destination, target, reference, plane, x, y, width, height, warp, blend);
                return;
            }
            Av1SequenceHeader source = reference.PixelGeometry;
            if (source.Width > target.Width * 2 || source.Height > target.Height * 2 ||
                target.Width > source.Width * 16 || target.Height > source.Height * 16)
            {
                throw new InvalidDataException("The AV1 reference scaling ratio exceeds the permitted range.");
            }
            int sub = target.PlaneShift(plane);
            int sourceWidth = (source.Width + sub) >> sub;
            int sourceHeight = (source.Height + sub) >> sub;
            int sourceStride = source.PlaneStride(plane);
            ReadOnlySpan<byte> pixels = reference.Pixels.Memory.Span[source.PlaneOffset(plane)..];
            int scaleX = ((source.Width << 14) + target.Width / 2) / target.Width;
            int scaleY = ((source.Height << 14) + target.Height / 2) / target.Height;
            int startX = Scale(x, motion.Column, sub, scaleX);
            int startY = Scale(y, motion.Row, sub, scaleY);
            int stepX = (scaleX + 8) >> 4;
            int stepY = (scaleY + 8) >> 4;
            if (stepX == 1024 && stepY == 1024 && (startX & 1023) == 32 && (startY & 1023) == 32)
            {
                if (blend.Active)
                {
                    for (int row = 0; row < height; row++)
                    {
                        int sourceY = Math.Clamp((startY >> 10) + row, 0, sourceHeight - 1);
                        Span<byte> result = destination.Row(x, y + row, width);
                        for (int column = 0; column < width; column++)
                        {
                            int sourceX = Math.Clamp((startX >> 10) + column, 0, sourceWidth - 1);
                            int value = pixels[sourceY * sourceStride + sourceX] << 4;
                            byte color = blend.Apply(row, column, width, value);
                            if (blend.Second) { result[column] = color; }
                        }
                    }
                    return;
                }
                CopyInteger(pixels, sourceStride, sourceWidth, sourceHeight, destination,
                    x, y, width, height, startX >> 10, startY >> 10);
                return;
            }
            int filterX = SelectFilter(motion.FilterX, predictionWidth);
            int filterY = SelectFilter(motion.FilterY, predictionHeight);
            Span<int> intermediate = stackalloc int[8 * 128];
            Span<int> cachedRows = stackalloc int[8];
            cachedRows.Fill(int.MinValue);
            for (int row = 0; row < height; row++)
            {
                int positionY = startY + row * stepY;
                int sourceY = positionY >> 10;
                for (int tap = 0; tap < 8; tap++)
                {
                    int sampleY = sourceY + tap - 3;
                    int cache = sampleY & 7;
                    if (cachedRows[cache] == sampleY) { continue; }
                    int offset = Math.Clamp(sampleY, 0, sourceHeight - 1) * sourceStride;
                    Span<int> line = intermediate.Slice(cache * 128, width);
                    for (int column = 0; column < width; column++)
                    {
                        int positionX = startX + column * stepX;
                        ReadOnlySpan<short> horizontal = Av1InterpolationFilters.Values.Slice((filterX * 16 + ((positionX >> 6) & 15)) * 8, 8);
                        int sum = 0;
                        for (int index = 0; index < 8; index++)
                        {
                            int sampleX = Math.Clamp((positionX >> 10) + index - 3, 0, sourceWidth - 1);
                            sum += horizontal[index] * pixels[offset + sampleX];
                        }
                        line[column] = (sum + 4) >> 3;
                    }
                    cachedRows[cache] = sampleY;
                }
                ReadOnlySpan<short> vertical = Av1InterpolationFilters.Values.Slice((filterY * 16 + ((positionY >> 6) & 15)) * 8, 8);
                Span<byte> result = destination.Row(x, y + row, width);
                for (int column = 0; column < width; column++)
                {
                    int sum = 0;
                    for (int tap = 0; tap < 8; tap++)
                    {
                        sum += vertical[tap] * intermediate[((sourceY + tap - 3) & 7) * 128 + column];
                    }
                    if (!blend.Active) { result[column] = (byte)Math.Clamp((sum + 1024) >> 11, 0, 255); }
                    else
                    {
                        byte color = blend.Apply(row, column, width, (sum + 64) >> 7);
                        if (blend.Second) { result[column] = color; }
                    }
                }
            }
        }

        private static void CopyInteger(ReadOnlySpan<byte> source, int sourceStride, int sourceWidth, int sourceHeight,
            Av1PredictionDestination destination, int x, int y, int width, int height, int sourceX, int sourceY)
        {
            int left = Math.Min(width, Math.Max(0, -sourceX));
            int start = Math.Clamp(sourceX, 0, sourceWidth - 1);
            int count = Math.Min(width - left, sourceWidth - start);
            for (int row = 0; row < height; row++)
            {
                ReadOnlySpan<byte> input = source.Slice(Math.Clamp(sourceY + row, 0, sourceHeight - 1) * sourceStride, sourceWidth);
                Span<byte> output = destination.Row(x, y + row, width);
                output[..left].Fill(input[0]);
                input.Slice(start, count).CopyTo(output[left..]);
                output[(left + count)..].Fill(input[^1]);
            }
        }

        private static int Scale(int position, int motion, int subsampling, int scale)
        {
            long original = (position << 4) + ((2 * motion) >> subsampling) + 8;
            long scaled = original * scale - (8 << 14);
            int rounded = checked((int)((Math.Abs(scaled) + 128) >> 8));
            return (scaled < 0 ? -rounded : rounded) + 32;
        }

        private static int SelectFilter(int filter, int extent) => filter switch
        {
            0 or 2 when extent <= 4 => 4,
            1 when extent <= 4 => 5,
            0 or 1 or 2 or 3 => filter,
            _ => throw new InvalidDataException("The AV1 interpolation filter is invalid.")
        };
    }
}
