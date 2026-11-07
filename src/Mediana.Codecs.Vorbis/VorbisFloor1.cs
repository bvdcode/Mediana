// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisFloor1 : VorbisFloor
    {
        private readonly WritableMediaBuffer storage;
        private readonly int partitions, classes, points, multiplier, range;
        private ReadOnlySpan<int> Data => MemoryMarshal.Cast<byte, int>(storage.Span);
        private int PointsOffset => partitions + 11 * classes;

        public VorbisFloor1(scoped VorbisFloor1Definition definition, MediaBufferAllocator allocator)
        {
            partitions = definition.Partitions.Length; classes = definition.Dimensions.Length;
            points = definition.Points.Length; multiplier = definition.Multiplier;
            range = multiplier switch { 1 => 256, 2 => 128, 3 => 86, 4 => 64, _ => throw new InvalidDataException("The Vorbis floor multiplier is invalid.") };
            storage = allocator.Allocate(checked((partitions + 11 * classes + 4 * points) * sizeof(int)));
            Span<int> data = MemoryMarshal.Cast<byte, int>(storage.Span);
            Copy(definition.Partitions, data); Copy(definition.Dimensions, data[partitions..]);
            Copy(definition.Subclasses, data[(partitions + classes)..]); Copy(definition.Masterbooks, data[(partitions + 2 * classes)..]);
            definition.SubclassBooks.CopyTo(data[(partitions + 3 * classes)..]);
            for (int index = 0; index < points; index++)
            {
                data[PointsOffset + index] = definition.Points[index];
                data[PointsOffset + 3 * points + index] = index;
                if (index < 2) { continue; }
                int low = 0, high = 1, x = definition.Points[index];
                for (int previous = 0; previous < index; previous++)
                {
                    int candidate = definition.Points[previous];
                    if (candidate < x && candidate > definition.Points[low]) { low = previous; }
                    if (candidate > x && candidate < definition.Points[high]) { high = previous; }
                }
                data[PointsOffset + points + index] = low;
                data[PointsOffset + 2 * points + index] = high;
            }
            Span<int> sorted = data.Slice(PointsOffset + 3 * points, points);
            for (int index = 1; index < points; index++)
            {
                int entry = sorted[index], at = index;
                while (at > 0 && definition.Points[sorted[at - 1]] > definition.Points[entry]) { sorted[at] = sorted[at - 1]; at--; }
                sorted[at] = entry;
            }
        }

        public override bool Decode(ref VorbisBitReader reader, IReadOnlyList<VorbisCodebook> books,
            Span<float> curve, Span<float> vector, CancellationToken token)
        {
            curve.Clear();
            if (!reader.TryRead(1, out uint present) || present == 0) { return false; }
            Span<int> y = stackalloc int[250]; y[..points].Clear();
            int bits = VorbisBitReader.ILog((uint)range - 1);
            if (!reader.TryRead(bits, out uint first) || !reader.TryRead(bits, out uint last)) { return false; }
            y[0] = (int)first; y[1] = (int)last;
            ReadOnlySpan<int> data = Data;
            int point = 2;
            for (int partition = 0; partition < partitions; partition++)
            {
                token.ThrowIfCancellationRequested();
                int type = data[partition], dimension = data[partitions + type], subbits = data[partitions + classes + type], selector = 0;
                if (subbits != 0 && !books[data[partitions + 2 * classes + type]].TryDecode(ref reader, out selector)) { return false; }
                for (int index = 0; index < dimension; index++)
                {
                    int book = data[partitions + 3 * classes + type * 8 + (selector & ((1 << subbits) - 1))];
                    selector >>= subbits;
                    if (book >= 0 && !books[book].TryDecode(ref reader, out y[point])) { return false; }
                    point++;
                }
            }
            ReadOnlySpan<int> x = data.Slice(PointsOffset, points);
            for (int index = 2; index < points; index++)
            {
                int low = data[PointsOffset + points + index], high = data[PointsOffset + 2 * points + index];
                int predicted = RenderPoint(x[low], x[high], y[low] & 0x7fff, y[high] & 0x7fff, x[index]);
                int value = y[index], highRoom = range - predicted, lowRoom = predicted;
                if (value == 0) { y[index] = predicted | 0x8000; continue; }
                int room = 2 * Math.Min(highRoom, lowRoom);
                if (value >= room) { value = highRoom > lowRoom ? value - lowRoom : highRoom - value - 1; }
                else { value = (value & 1) != 0 ? -(value + 1) / 2 : value / 2; }
                y[index] = (predicted + value) & 0x7fff;
                y[low] &= 0x7fff; y[high] &= 0x7fff;
            }
            int previousX = 0, previousY = y[0] * multiplier;
            for (int index = 1; index < points; index++)
            {
                int current = data[PointsOffset + 3 * points + index];
                if ((y[current] & 0x8000) != 0) { continue; }
                int currentY = y[current] * multiplier;
                RenderLine(curve, previousX, x[current], previousY, currentY);
                previousX = x[current]; previousY = currentY;
            }
            if (previousX < curve.Length) { curve[previousX..].Fill(Gain(previousY)); }
            return true;
        }

        private static int RenderPoint(int x0, int x1, int y0, int y1, int x)
        {
            int difference = y1 - y0;
            int offset = Math.Abs(difference) * (x - x0) / (x1 - x0);
            return difference < 0 ? y0 - offset : y0 + offset;
        }

        private static void RenderLine(Span<float> curve, int x0, int x1, int y0, int y1)
        {
            int width = x1 - x0, difference = y1 - y0, step = difference / width;
            int residual = Math.Abs(difference) - Math.Abs(step) * width, error = 0;
            int extra = difference < 0 ? step - 1 : step + 1;
            for (int x = x0, y = y0; x < Math.Min(x1, curve.Length); x++)
            {
                curve[x] = Gain(y);
                error += residual;
                if (error >= width) { error -= width; y += extra; }
                else { y += step; }
            }
        }

        private static float Gain(int value)
        {
            if ((uint)value > 255) { throw new InvalidDataException("A Vorbis floor amplitude is outside its quantized range."); }
            return VorbisFloor1GainTable.Values[value];
        }

        private static void Copy(ReadOnlySpan<byte> source, Span<int> destination)
        {
            for (int index = 0; index < source.Length; index++) { destination[index] = source[index]; }
        }

        public override void Dispose() => storage.Dispose();
    }
}
