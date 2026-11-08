// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void PredictInter(int row, int column, int width, int height, bool hasChroma, Av1MotionBlock block, Av1ReferencePictures pictures, Av1CompoundPrediction prediction, Av1WarpParameters warp)
        {
            int maskStride = width * 4;
            Span<byte> mask = prediction.Mask == Av1CompoundMaskType.None ? default : stackalloc byte[maskStride * height * 4];
            if (prediction.Mask == Av1CompoundMaskType.Wedge) { Av1WedgeMask.Initialize(mask, maskStride, height * 4, prediction.Index, prediction.Sign); }
            int planes = hasChroma ? 3 : 1;
            for (int plane = 0; plane < planes; plane++)
            {
                int sub = sequence.PlaneShift(plane);
                int x = (column >> sub) * 4;
                int y = (row >> sub) * 4;
                int planeWidth = Math.Max(4, (width * 4) >> sub);
                int planeHeight = Math.Max(4, (height * 4) >> sub);
                bool subBlocks = plane != 0 && (width == 1 || height == 1);
                int candidateRow = (row >> sub) << sub;
                int candidateColumn = (column >> sub) << sub;
                if (subBlocks)
                {
                    for (int dy = 0; dy < planeHeight / 2; dy++)
                    {
                        for (int dx = 0; dx < planeWidth / 2; dx++)
                        {
                            if (motionBlocks[(candidateRow + dy) * sequence.MiColumns + candidateColumn + dx].Reference == 0)
                            {
                                subBlocks = false;
                            }
                        }
                    }
                }
                int stepX = subBlocks ? width * 2 : planeWidth;
                int stepY = subBlocks ? height * 2 : planeHeight;
                for (int dy = 0; dy < planeHeight; dy += stepY)
                {
                    for (int dx = 0; dx < planeWidth; dx += stepX)
                    {
                        Av1MotionBlock selected = block;
                        if (subBlocks)
                        {
                            selected = motionBlocks[(candidateRow + dy / stepY) * sequence.MiColumns + candidateColumn + dx / stepX];
                        }
                        Av1StoredPicture reference = pictures.Get(header.Inter.References[selected.Reference - 1]);
                        int clippedWidth = Math.Min(stepX, sequence.PlaneStride(plane) - x - dx);
                        int clippedHeight = Math.Min(stepY, sequence.PlaneHeight(plane) - y - dy);
                        if (clippedWidth > 0 && clippedHeight > 0)
                        {
                            Av1WarpParameters firstWarp = default;
                            if (!header.Inter.IntegerMotion && planeWidth >= 8 && planeHeight >= 8)
                            {
                                if (selected.MotionMode == 2) { firstWarp = warp; }
                                else { firstWarp = GlobalWarp(selected, selected.Reference, reference); }
                            }
                            if (selected.SecondReference == 0 && firstWarp.IsValid)
                            {
                                Av1WarpPrediction.Apply(output, sequence, reference, plane, x + dx, y + dy, clippedWidth, clippedHeight, firstWarp);
                            }
                            else if (selected.SecondReference == 0)
                            {
                                Av1MotionCompensation.Apply(output, sequence, reference, plane, x + dx, y + dy, clippedWidth, clippedHeight, selected, stepX, stepY);
                            }
                            else
                            {
                                Av1StoredPicture second = pictures.Get(header.Inter.References[selected.SecondReference - 1]);
                                Av1WarpParameters secondWarp = !header.Inter.IntegerMotion && planeWidth >= 8 && planeHeight >= 8
                                    ? GlobalWarp(selected, selected.SecondReference, second) : default;
                                Av1MotionCompensation.ApplyCompound(output, sequence, reference, second, plane, x + dx, y + dy, clippedWidth, clippedHeight, selected, prediction, stepX, stepY, mask, maskStride, firstWarp, secondWarp);
                            }
                        }
                    }
                }
            }
        }

        private Av1WarpParameters GlobalWarp(Av1MotionBlock block, int reference, Av1StoredPicture picture)
        {
            if (block.Mode is not (15 or 23)) { return default; }
            Av1SequenceHeader source = picture.PixelGeometry;
            int scaleX = ((source.Width << 14) + sequence.Width / 2) / sequence.Width;
            int scaleY = ((source.Height << 14) + sequence.Height / 2) / sequence.Height;
            if (scaleX != 16384 || scaleY != 16384) { return default; }
            return header.GlobalMotion[reference].Warp;
        }
    }
}
