// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264ReferencePicture(H264Picture Picture, int FrameNumber, H264PictureOrder Order, int? LongTermIndex = null)
    {
        public H264PictureIdentity FrameIdentity { get; init; } = Picture.Identity;
        public H264PictureStructure Structure { get; init; } = Picture.Structure;
        public H264Picture? Companion { get; init; }
        public H264PictureIdentity Identity => FrameIdentity.ForStructure(Structure);

        public int PictureNumber(int currentFrameNumber, int maximumFrameNumber) =>
            FrameNumber > currentFrameNumber ? FrameNumber - maximumFrameNumber : FrameNumber;

        public H264ReferencePicture GetField(H264PictureStructure structure)
        {
            if (structure is not (H264PictureStructure.TopField or H264PictureStructure.BottomField))
            {
                throw new ArgumentOutOfRangeException(nameof(structure));
            }
            if (Structure == structure) { return this; }
            if (Structure != H264PictureStructure.Frame)
            {
                throw new InvalidOperationException("Only a frame reference can be split into fields.");
            }
            H264PictureOrder order = structure switch
            {
                H264PictureStructure.TopField => new(Order.Top, 0, structure),
                H264PictureStructure.BottomField => new(0, Order.Bottom, structure),
                _ => throw new ArgumentOutOfRangeException(nameof(structure))
            };
            return this with { Picture = SelectFieldPicture(structure), Structure = structure, Order = order, Companion = null };
        }

        private H264Picture SelectFieldPicture(H264PictureStructure structure)
        {
            if (Picture.Structure == H264PictureStructure.Frame && Companion is null)
            {
                return Picture;
            }
            if (Picture.Structure == H264PictureStructure.TopField &&
                Companion is { Structure: H264PictureStructure.BottomField } bottom && bottom.Sequence == Picture.Sequence)
            {
                return structure switch
                {
                    H264PictureStructure.TopField => Picture,
                    H264PictureStructure.BottomField => bottom,
                    _ => throw new ArgumentOutOfRangeException(nameof(structure))
                };
            }
            throw new InvalidOperationException("The frame reference does not contain matching complementary fields.");
        }

        public static H264ReferencePicture GetFrame(H264ReferencePicture top, H264ReferencePicture bottom)
        {
            if (top.Structure != H264PictureStructure.TopField || bottom.Structure != H264PictureStructure.BottomField ||
                top.FrameIdentity != bottom.FrameIdentity || top.FrameNumber != bottom.FrameNumber || top.LongTermIndex != bottom.LongTermIndex)
            {
                throw new InvalidDataException("A frame reference requires complementary fields with matching reference marking.");
            }
            H264Picture? companion = null;
            if (!ReferenceEquals(top.Picture, bottom.Picture)) { companion = bottom.Picture; }
            return top with { Structure = H264PictureStructure.Frame, Order = new(top.Order.Top, bottom.Order.Bottom), Companion = companion };
        }
    }
}
