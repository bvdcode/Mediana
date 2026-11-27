// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Metadata
{
    internal class RawPreviewSelection(int requestedSize)
    {
        private double bestArea;
        private bool sufficient;

        internal bool Consider(double width, double height)
        {
            bool fits = Math.Max(width, height) >= requestedSize;
            double area = width * height;
            bool improves = fits ? !sufficient || area < bestArea : !sufficient && area > bestArea;
            if (!improves) { return false; }
            bestArea = area;
            sufficient = fits;
            return true;
        }
    }
}
