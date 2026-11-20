// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264AdaptiveReferences
    {
        private readonly H264BidirectionalReferences frames;
        private readonly H264BidirectionalReferences top;
        private readonly H264BidirectionalReferences bottom;

        public H264AdaptiveReferences(H264BidirectionalReferences frames)
        {
            ArgumentNullException.ThrowIfNull(frames);
            this.frames = frames;
            if (frames.L0.Count == 0 && frames.L1.Count == 0)
            {
                top = frames;
                bottom = frames;
                return;
            }
            (H264ReferencePicture?[] top0, H264ReferencePicture?[] bottom0) = Expand(frames.L0);
            (H264ReferencePicture?[] top1, H264ReferencePicture?[] bottom1) = Expand(frames.L1);
            top = new(top0, top1);
            bottom = new(bottom0, bottom1);
        }

        public H264BidirectionalReferences Get(H264PictureStructure structure) => structure switch
        {
            H264PictureStructure.Frame => frames,
            H264PictureStructure.TopField => top,
            H264PictureStructure.BottomField => bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(structure))
        };

        private static (H264ReferencePicture?[] Top, H264ReferencePicture?[] Bottom) Expand(IReadOnlyList<H264ReferencePicture?> frames)
        {
            if (frames.Count > H264ReferenceSyntax.MaximumFrameReferences)
            {
                throw new InvalidDataException("An MBAFF frame reference list cannot exceed 16 entries.");
            }
            H264ReferencePicture?[] top = new H264ReferencePicture?[frames.Count * 2];
            H264ReferencePicture?[] bottom = new H264ReferencePicture?[top.Length];
            for (int index = 0; index < frames.Count; index++)
            {
                H264ReferencePicture? frame = frames[index];
                if (frame is null)
                {
                    continue;
                }
                if (frame.Structure != H264PictureStructure.Frame)
                {
                    throw new InvalidDataException("MBAFF reference expansion requires frame references.");
                }
                H264ReferencePicture first = frame.GetField(H264PictureStructure.TopField);
                H264ReferencePicture second = frame.GetField(H264PictureStructure.BottomField);
                top[index * 2] = first;
                top[index * 2 + 1] = second;
                bottom[index * 2] = second;
                bottom[index * 2 + 1] = first;
            }
            return (top, bottom);
        }
    }
}
