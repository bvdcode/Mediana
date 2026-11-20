// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal class H264QueuedField(VideoFrame frame, H264PictureOrder order, H264SliceHeader header) : IDisposable
    {
        public VideoFrame Frame { get; } = frame;
        public H264PictureOrder Order { get; } = order;
        public H264SliceHeader Header { get; } = header;

        public bool Complements(H264SliceHeader next) => Header.Sequence == next.Sequence &&
            H264FieldPairing.IsCandidate(Header.Structure, H264FieldPairing.ResetsReferences(Header) ? 0 : Header.FrameNumber,
                Header.ReferenceIndicator != 0, next) && !H264FieldPairing.ResetsReferences(next);

        public void Dispose() => Frame.Dispose();
    }
}
