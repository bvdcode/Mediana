// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264ReferencePicture(H264Picture Picture, int FrameNumber, H264PictureOrder Order, int? LongTermIndex = null)
    {
        public int PictureNumber(int currentFrameNumber, int maximumFrameNumber) =>
            FrameNumber > currentFrameNumber ? FrameNumber - maximumFrameNumber : FrameNumber;
    }
}
