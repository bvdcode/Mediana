// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Codecs.Compression
{
    internal static class FaxDecoder
    {
        private static readonly Dictionary<int, int>[] Runs = CreateCodes();
        private static Dictionary<int, int>[] CreateCodes()
        {
            Dictionary<int, int>[] result = [[], []];
            using StringReader reader = new(FaxCodeData.Text);
            reader.ReadLine();
            while (reader.ReadLine() is string line)
            {
                string[] columns = line.Split(',');
                for (int color = 0; color < 2; color++)
                {
                    string bits = columns[color + 1];
                    result[color].Add((1 << bits.Length) | Convert.ToInt32(bits, 2), int.Parse(columns[0], CultureInfo.InvariantCulture));
                }
            }
            return result;
        }
        internal static void Decode(ReadOnlySpan<byte> encoded, Span<byte> output, int width, int height,
            int compression, uint options, bool lowBitFirst, Span<int> changes, CancellationToken token)
        {
            if ((compression == 3 && (options & ~5u) != 0) || (compression == 4 && options != 0))
            { throw new NotSupportedException("CCITT uncompressed extension mode is not supported."); }
            int stride = checked((int)(((long)width + 7) / 8));
            if (output.Length != checked(stride * height) || changes.Length < checked((width + 2) * 2))
            { throw new ArgumentException("CCITT sample workspace dimensions disagree."); }
            output.Clear();
            Span<int> reference = changes[..(width + 2)], current = changes.Slice(width + 2, width + 2);
            int referenceCount = 2; reference[0] = reference[1] = width;
            FaxBitReader input = new(encoded, lowBitFirst);
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                bool oneDimensional = compression == 2;
                if (compression == 3) { input.EndOfLine(); oneDimensional = (options & 1) == 0 || input.Bit() != 0; }
                if (y == 0 && compression == 3 && !oneDimensional) { throw new InvalidDataException("The first T.4 row must be one-dimensional."); }
                Span<byte> row = output.Slice(y * stride, stride);
                int a0 = 0, color = 0; bool start = true;
                long steps = 0;
                while (a0 < width)
                {
                    if (++steps > (long)width * 4 + 100) { throw new InvalidDataException("CCITT row makes no progress."); }
                    if (oneDimensional)
                    {
                        int end = a0 + Run(ref input, color, width - a0);
                        if (color != 0) { Paint(row, a0, end); }
                        a0 = end; color ^= 1;
                    }
                    else
                    {
                        int mode = Mode(ref input), index = Reference(reference[..referenceCount], a0, color, start);
                        int b1 = reference[index];
                        if (mode == 10)
                        {
                            int a1 = a0 + Run(ref input, color, width - a0);
                            int a2 = a1 + Run(ref input, 1 - color, width - a1);
                            if (color != 0) { Paint(row, a0, a1); } else { Paint(row, a1, a2); }
                            a0 = a2;
                        }
                        else if (mode == 11)
                        {
                            int end = reference[Math.Min(index + 1, referenceCount - 1)];
                            if (end < a0) { throw new InvalidDataException("Invalid CCITT pass mode."); }
                            if (color != 0) { Paint(row, a0, end); }
                            a0 = end;
                        }
                        else
                        {
                            int end = b1 + mode;
                            if (end < a0 || end > width) { throw new InvalidDataException("Invalid CCITT vertical mode."); }
                            if (color != 0) { Paint(row, a0, end); }
                            a0 = end; color ^= 1;
                        }
                    }
                    start = false;
                }
                if (compression == 2) { input.Align(); }
                int count = 0, previous = 0;
                for (int x = 0; x < width; x++)
                {
                    int value = (row[x >> 3] >> (7 - (x & 7))) & 1;
                    if (value != previous) { current[count++] = x; previous = value; }
                }
                current[count++] = width; current[count++] = width;
                Span<int> swap = reference; reference = current; current = swap; referenceCount = count;
            }
        }
        private static void Paint(Span<byte> row, int start, int end)
        {
            while (start < end && (start & 7) != 0) { row[start >> 3] |= (byte)(128 >> (start & 7)); start++; }
            int bytes = (end - start) / 8;
            row.Slice(start >> 3, bytes).Fill(255); start += bytes * 8;
            while (start < end) { row[start >> 3] |= (byte)(128 >> (start & 7)); start++; }
        }
        private static int Reference(ReadOnlySpan<int> changes, int a0, int color, bool start)
        {
            int low = 0, high = changes.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (changes[middle] < a0 || (!start && changes[middle] == a0)) { low = middle + 1; }
                else { high = middle; }
            }
            if ((low & 1) != color) { low++; }
            return Math.Min(low, changes.Length - 2);
        }
        private static int Run(ref FaxBitReader input, int color, int maximum)
        {
            int total = 0;
            while (true)
            {
                int code = 1, run = -1;
                for (int length = 1; length <= 13; length++)
                {
                    code = (code << 1) | input.Bit();
                    if (Runs[color].TryGetValue(code, out run)) { break; }
                    run = -1;
                }
                if (run < 0 || run > maximum - total) { throw new InvalidDataException("Invalid CCITT run."); }
                total += run;
                if (run < 64) { return total; }
            }
        }
        private static int Mode(ref FaxBitReader input)
        {
            int code = 1;
            for (int length = 1; length <= 7; length++)
            {
                code = (code << 1) | input.Bit();
                int? mode = code switch { 3 => 0, 9 => 10, 10 => -1, 11 => 1, 17 => 11, 66 => -2, 67 => 2, 130 => -3, 131 => 3, _ => null };
                if (mode is int value) { return value; }
            }
            throw new NotSupportedException("Unsupported CCITT extension mode.");
        }
    }
}
