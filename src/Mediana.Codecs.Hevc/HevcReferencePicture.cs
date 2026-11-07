// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcReferencePicture(DecodedPicture pixels, int order, HevcMotionField motion) : IDisposable
    {
        public DecodedPicture Pixels { get; } = pixels;
        public int Order { get; } = order;
        public HevcMotionField Motion { get; } = motion;

        public void Dispose()
        {
            Pixels.Dispose(); Motion.Dispose();
        }
    }
}
