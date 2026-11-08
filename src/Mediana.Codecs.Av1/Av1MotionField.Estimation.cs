// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1MotionField
    {
        internal static void Estimate(Av1SequenceHeader sequence, Av1FrameHeader header, Av1ReferencePictures references,
            Span<Av1MotionFieldSample> field, CancellationToken cancellationToken)
        {
            if (field.Length != sequence.MiColumns * sequence.MiRows / 4)
            {
                throw new ArgumentException("The AV1 projected motion-field workspace has an invalid extent.");
            }
            field.Clear();
            Av1StoredPicture last = references.Get(header.Inter.References[0]);
            if (last.AlternateOrderHint != references.OrderHint(header.Inter.References[3]))
            {
                ProjectReference(1, -1, sequence, header, references, field, cancellationToken);
            }
            int remaining = 1;
            for (int reference = 5; reference <= 7; reference++)
            {
                int hint = references.OrderHint(header.Inter.References[reference - 1]);
                if (Av1ReferenceOrdering.Distance(hint, header.OrderHint, sequence.Tools.OrderHintBits) > 0 &&
                    (reference != 7 || remaining >= 0) && ProjectReference(reference, 1, sequence, header, references, field, cancellationToken))
                {
                    remaining--;
                }
            }
            if (remaining >= 0) { ProjectReference(2, -1, sequence, header, references, field, cancellationToken); }
        }

        private static bool ProjectReference(int reference, int sign, Av1SequenceHeader sequence, Av1FrameHeader header,
            Av1ReferencePictures references, Span<Av1MotionFieldSample> field, CancellationToken cancellationToken)
        {
            Av1StoredPicture picture = references.Get(header.Inter.References[reference - 1]);
            Av1SequenceHeader source = picture.Header.Geometry;
            if (source.MiColumns != sequence.MiColumns || source.MiRows != sequence.MiRows || picture.Header.FrameType is Av1FrameType.Key or Av1FrameType.IntraOnly)
            {
                return false;
            }
            int distance = Av1ReferenceOrdering.Distance(picture.Header.OrderHint, header.OrderHint, sequence.Tools.OrderHintBits);
            if (Math.Abs(distance) > MaximumDistance) { return true; }
            ReadOnlySpan<Av1MotionFieldSample> saved = MemoryMarshal.Cast<byte, Av1MotionFieldSample>(
                (picture.MotionField ?? throw new InvalidDataException("The AV1 reference motion-field state is missing.")).Memory.Span);
            int columns = sequence.MiColumns >> 1;
            int rows = sequence.MiRows >> 1;
            if (saved.Length != rows * columns) { throw new InvalidDataException("The AV1 reference motion-field extent is invalid."); }
            for (int y = 0; y < rows; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < columns; x++)
                {
                    Av1MotionFieldSample sample = saved[y * columns + x];
                    if (!sample.Valid) { continue; }
                    int projectedY = Position(y, Project(sample.Row, distance * sign, sample.Distance), sign, rows, 0);
                    int projectedX = Position(x, Project(sample.Column, distance * sign, sample.Distance), sign, columns, 8);
                    if (projectedY >= 0 && projectedX >= 0) { field[projectedY * columns + projectedX] = sample; }
                }
            }
            return true;
        }

        internal static int Position(int origin, int vector, int sign, int extent, int maximumOffset)
        {
            int basePosition = (origin >> 3) << 3;
            int offset = Math.Abs(vector) >> 6;
            if (vector < 0) { offset = -offset; }
            int target = origin + sign * offset;
            if (target < 0 || target >= extent || target < basePosition - maximumOffset || target >= basePosition + 8 + maximumOffset) { return -1; }
            return target;
        }
    }
}
