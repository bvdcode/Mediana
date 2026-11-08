// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1LoopFilter
    {
        internal static void Apply(Span<byte> pixels, Av1SequenceHeader sequence, Av1LoopFilterParameters parameters,
            ReadOnlySpan<Av1FilterBlock> blocks, CancellationToken cancellationToken, Av1Segmentation segmentation = default)
        {
            if (!parameters.Enabled) { return; }
            if (blocks.Length != sequence.MiColumns * sequence.MiRows || pixels.Length != sequence.SampleCount)
            {
                throw new ArgumentException("The AV1 loop-filter buffers have invalid extents.");
            }
            int planes = sequence.Monochrome ? 1 : 3;
            for (int plane = 0; plane < planes; plane++)
            {
                if ((plane == 1 && parameters.U == 0) || (plane == 2 && parameters.V == 0)) { continue; }
                int sub = sequence.PlaneShift(plane);
                int step = 1 << sub;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int row = 0; row < sequence.MiRows; row += step)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int column = 0; column < sequence.MiColumns; column += step)
                        {
                            Edge(pixels, sequence, parameters, blocks, plane, pass, row, column, segmentation);
                        }
                    }
                }
            }
        }

        private static void Edge(Span<byte> pixels, Av1SequenceHeader sequence, Av1LoopFilterParameters parameters,
            ReadOnlySpan<Av1FilterBlock> blocks, int plane, int pass, int row, int column, Av1Segmentation segmentation)
        {
            int x = column * 4;
            int y = row * 4;
            if (x >= sequence.Width || y >= sequence.Height || (pass == 0 && x == 0) || (pass == 1 && y == 0)) { return; }
            int sub = sequence.PlaneShift(plane);
            row |= sub;
            column |= sub;
            int previousRow = row - (pass << sub);
            int previousColumn = column - ((1 - pass) << sub);
            Av1FilterBlock current = blocks[row * sequence.MiColumns + column];
            Av1FilterBlock previous = blocks[previousRow * sequence.MiColumns + previousColumn];
            Av1TransformSize transform = Av1TransformSize.FromIndex(current.Transform(plane));
            Av1TransformSize previousTransform = Av1TransformSize.FromIndex(previous.Transform(plane));
            int planeX = x >> sub;
            int planeY = y >> sub;
            int coordinate = pass == 0 ? planeX : planeY;
            int transformExtent = pass == 0 ? transform.Width : transform.Height;
            if (coordinate % transformExtent != 0) { return; }
            int blockExtent = Math.Max(4, ((pass == 0 ? current.Width4 : current.Height4) * 4) >> sub);
            if (coordinate % blockExtent != 0 && current.Skip && current.Reference != 0) { return; }
            int level = parameters.Level(plane, pass, current, segmentation);
            if (level == 0) { level = parameters.Level(plane, pass, previous, segmentation); }
            if (level == 0) { return; }
            int shift = parameters.Sharpness switch { > 4 => 2, > 0 => 1, _ => 0 };
            int limit = parameters.Sharpness > 0 ? Math.Clamp(level >> shift, 1, 9 - parameters.Sharpness) : Math.Max(1, level);
            int boundaryLimit = 2 * (level + 2) + limit;
            int previousExtent = pass == 0 ? previousTransform.Width : previousTransform.Height;
            int filterSize = Math.Min(plane == 0 ? 16 : 8, Math.Min(transformExtent, previousExtent));
            int stride = sequence.PlaneStride(plane);
            int offset = sequence.PlaneOffset(plane) + planeY * stride + planeX;
            int across = pass == 0 ? 1 : stride;
            int along = pass == 0 ? stride : 1;
            for (int index = 0; index < 4; index++)
            {
                Av1LoopFilterKernel.Apply(pixels, offset + index * along, across, filterSize, plane != 0, limit, boundaryLimit, level >> 4);
            }
        }
    }
}
