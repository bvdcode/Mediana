// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaBlockHeader(ulong TrackNumber, short RelativeTimestamp,
        byte Flags, MatroskaLacingMode Lacing, int FrameCount, long PayloadOffset, bool IsSimpleBlock)
    {
        public bool IsInvisible => (Flags & 8) != 0;
        public bool? IsKeyFrame
        {
            get
            {
                if (IsSimpleBlock)
                {
                    return (Flags & 0x80) != 0;
                }
                return null;
            }
        }
    }
}
