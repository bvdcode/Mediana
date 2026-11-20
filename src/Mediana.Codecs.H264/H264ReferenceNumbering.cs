// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264ReferenceNumbering(H264SliceHeader Header)
    {
        public int MaximumFrameNumber => 1 << Header.Sequence.FrameNumberBits;
        public int MaximumPictureNumber => Header.Layout.MaximumPictureNumber;
        public int CurrentPictureNumber => Header.Layout.IsField ? 2 * Header.FrameNumber + 1 : Header.FrameNumber;

        public int ShortTerm(H264ReferencePicture reference)
        {
            int wrapped = reference.PictureNumber(Header.FrameNumber, MaximumFrameNumber);
            return Header.Layout.IsField ? 2 * wrapped + Parity(reference) : wrapped;
        }

        public int LongTerm(H264ReferencePicture reference)
        {
            int index = reference.LongTermIndex ?? throw new InvalidDataException("The H.264 reference has no long-term index.");
            return Header.Layout.IsField ? 2 * index + Parity(reference) : index;
        }

        private int Parity(H264ReferencePicture reference) => reference.Picture.Structure switch
        {
            H264PictureStructure.TopField or H264PictureStructure.BottomField => reference.Picture.Structure == Header.Structure ? 1 : 0,
            H264PictureStructure.Frame => throw new NotSupportedException("A frame reference must be separated into fields before field prediction."),
            _ => throw new ArgumentOutOfRangeException(nameof(reference))
        };
    }
}
