// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal static class H264FieldPairing
    {
        public static bool IsCandidate(H264PictureStructure previousStructure, int previousFrameNumber,
            bool previousReference, H264SliceHeader next) =>
            previousStructure != H264PictureStructure.Frame && next.Layout.IsField &&
            previousStructure != next.Structure && previousFrameNumber == next.FrameNumber &&
            previousReference == (next.ReferenceIndicator != 0) && !next.IsIdr;

        public static bool ResetsReferences(H264SliceHeader header) =>
            header.MemoryOperations.Any(operation => operation.Operation == 5);
    }
}
