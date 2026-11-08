// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1MotionField
    {
        private const int MaximumDistance = 31;
        private const int MaximumStoredComponent = (1 << 12) - 1;

        internal static int ByteLength(Av1SequenceHeader sequence) => checked(sequence.MiColumns * sequence.MiRows / 4 * Unsafe.SizeOf<Av1MotionFieldSample>());

        internal static int StoredLength(Av1SequenceHeader sequence, Av1FrameHeader header)
        {
            if (sequence.Tools.OrderHintBits == 0 || header.RefreshReferences == 0) { return 0; }
            return header.FrameType switch
            {
                Av1FrameType.Inter or Av1FrameType.Switch => ByteLength(sequence),
                Av1FrameType.Key or Av1FrameType.IntraOnly => 0,
                _ => throw new InvalidDataException("The AV1 frame type is invalid.")
            };
        }

        internal static void Store(Av1SequenceHeader sequence, Av1FrameHeader header, Av1ReferencePictures references,
            ReadOnlySpan<Av1MotionBlock> blocks, Span<byte> storage, CancellationToken cancellationToken = default)
        {
            if (storage.Length != StoredLength(sequence, header) || blocks.Length != sequence.MiColumns * sequence.MiRows)
            {
                throw new ArgumentException("The AV1 stored motion-field workspace has an invalid extent.");
            }
            Span<Av1MotionFieldSample> field = MemoryMarshal.Cast<byte, Av1MotionFieldSample>(storage);
            field.Clear();
            int columns = sequence.MiColumns >> 1;
            int rows = sequence.MiRows >> 1;
            for (int y = 0; y < rows; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < columns; x++)
                {
                    Av1MotionBlock block = blocks[(y * 2 + 1) * sequence.MiColumns + x * 2 + 1];
                    if (block.Reference == 0 || Math.Abs(block.Row) > MaximumStoredComponent || Math.Abs(block.Column) > MaximumStoredComponent) { continue; }
                    int hint = references.OrderHint(header.Inter.References[block.Reference - 1]);
                    int distance = -Av1ReferenceOrdering.Distance(hint, header.OrderHint, sequence.Tools.OrderHintBits);
                    if (distance is > 0 and <= MaximumDistance)
                    {
                        field[y * columns + x] = new((short)block.Row, (short)block.Column, (byte)distance);
                    }
                }
            }
        }

        internal static bool TryGet(ReadOnlySpan<Av1MotionFieldSample> field, Av1SequenceHeader sequence, Av1FrameHeader header,
            Av1ReferencePictures references, int reference, int row, int column, out Av1MotionCandidate candidate)
        {
            Av1MotionFieldSample sample = field[row * (sequence.MiColumns >> 1) + column];
            if (!sample.Valid) { candidate = default; return false; }
            int hint = references.OrderHint(header.Inter.References[reference - 1]);
            int distance = Av1ReferenceOrdering.Distance(header.OrderHint, hint, sequence.Tools.OrderHintBits);
            candidate = new(Project(sample.Row, distance, sample.Distance), Project(sample.Column, distance, sample.Distance), 2);
            return true;
        }

        internal static int Project(int value, int numerator, int denominator)
        {
            ReadOnlySpan<ushort> multipliers =
            [
                0, 16384, 8192, 5461, 4096, 3276, 2730, 2340, 2048, 1820, 1638,
                1489, 1365, 1260, 1170, 1092, 1024, 963, 910, 862, 819, 780,
                744, 712, 682, 655, 630, 606, 585, 564, 546, 528
            ];
            if (denominator <= 0) { throw new InvalidDataException("The AV1 temporal projection distance must be positive."); }
            long product = (long)value * Math.Clamp(numerator, -MaximumDistance, MaximumDistance) * multipliers[Math.Min(MaximumDistance, denominator)];
            int scaled = (int)((Math.Abs(product) + (1 << 13)) >> 14);
            if (product < 0) { scaled = -scaled; }
            return Math.Clamp(scaled, -(1 << 14) + 1, (1 << 14) - 1);
        }
    }
}
