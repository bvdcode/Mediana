// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264ColocatedBlock(H264Picture Picture, int Address, int X, int Y,
        H264PictureStructure Current, bool Field)
    {
        public int MapVerticalMotion(int value) => (Current != H264PictureStructure.Frame, Field) switch
        {
            (true, false) => value / 2,
            (false, true) => value * 2,
            (false, false) or (true, true) => value
        };

        public H264PictureIdentity MapIdentity(H264PictureIdentity identity) => (Current != H264PictureStructure.Frame, Field) switch
        {
            (true, false) => identity.ForStructure(Current),
            (false, true) => identity.Frame,
            (false, false) or (true, true) => identity
        };
    }
}
