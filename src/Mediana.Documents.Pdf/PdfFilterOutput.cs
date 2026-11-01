// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal ref struct PdfFilterOutput
    {
        private readonly Span<byte> destination;
        private readonly int limit;
        private readonly bool measuring;
        internal int Length { get; private set; }

        internal PdfFilterOutput(int limit)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(limit);
            this.limit = limit;
            measuring = true;
        }

        internal PdfFilterOutput(Span<byte> destination)
        {
            this.destination = destination;
            limit = destination.Length;
        }

        internal void Write(byte value)
        {
            Check(1);
            if (!measuring) { destination[Length] = value; }
            Length++;
        }

        internal void Write(scoped ReadOnlySpan<byte> bytes)
        {
            Check(bytes.Length);
            if (!measuring) { bytes.CopyTo(destination[Length..]); }
            Length += bytes.Length;
        }

        internal void Repeat(byte value, int count)
        {
            Check(count);
            if (!measuring) { destination.Slice(Length, count).Fill(value); }
            Length += count;
        }

        internal Span<byte> Reserve(int count)
        {
            Check(count);
            int start = Length;
            Length += count;
            if (measuring) { return []; }
            return destination.Slice(start, count);
        }

        private readonly void Check(int count)
        {
            if (count > limit - Length) { throw new InvalidDataException("PDF stream exceeds the decoded byte limit."); }
        }
    }
}
