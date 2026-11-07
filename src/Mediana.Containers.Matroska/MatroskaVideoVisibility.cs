// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Reads bounded VPx display syntax without decoding or retaining compressed frames.</summary>
    internal class MatroskaVideoVisibility(IRandomAccessByteSource source, MediaBufferAllocator allocator) : IDisposable
    {
        private readonly WritableMediaBuffer workspace = allocator.Allocate(Vp9SuperframeIndex.MaximumIndexBytes);
        private readonly Range[] members = new Range[Vp9SuperframeIndex.MaximumFrames];

        public async ValueTask<bool> IsVisibleAsync(MatroskaFrameRange frame, string codec, bool invisible,
            CancellationToken token)
        {
            bool visible;
            switch (codec)
            {
                case "vp8":
                    if (frame.Length < Vp8FrameTag.HeaderBytes) { throw new InvalidDataException("Truncated VP8 frame tag."); }
                    await source.ReadExactlyAtAsync(frame.Offset, workspace.Memory[..Vp8FrameTag.HeaderBytes], token).ConfigureAwait(false);
                    visible = Vp8FrameTag.Read(workspace.Memory.Span, frame.Length).ShowFrame;
                    break;
                case "vp9":
                    int tailBytes = Math.Min(frame.Length, Vp9SuperframeIndex.MaximumIndexBytes);
                    if (tailBytes == 0) { throw new InvalidDataException("A VP9 packet must contain a coded frame."); }
                    await source.ReadExactlyAtAsync(frame.Offset + frame.Length - tailBytes, workspace.Memory[..tailBytes], token).ConfigureAwait(false);
                    int count = Vp9SuperframeIndex.Read(workspace.Memory.Span[..tailBytes], frame.Length, members);
                    int displayed = 0;
                    for (int member = 0; member < count; member++)
                    {
                        (int offset, int length) = members[member].GetOffsetAndLength(frame.Length);
                        if (length == 0) { throw new InvalidDataException("A VP9 member must contain a frame header."); }
                        await source.ReadExactlyAtAsync(frame.Offset + offset, workspace.Memory[..1], token).ConfigureAwait(false);
                        if (Vp9FramePrefix.Read(workspace.Memory.Span[..1]).ShowFrame) { displayed++; }
                    }
                    if (displayed > 1)
                    { throw new NotSupportedException("A VP9 access unit must contain at most one visible picture with its presentation timestamp."); }
                    visible = displayed == 1;
                    break;
                default:
                    if (invisible) { throw new NotSupportedException("This Matroska codec does not expose invisible-picture semantics."); }
                    return true;
            }
            if (invisible && visible)
            { throw new InvalidDataException("The Matroska invisible flag disagrees with the coded picture's display flag."); }
            return visible;
        }

        public void Dispose() => workspace.Dispose();
    }
}
