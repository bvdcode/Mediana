// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264ColocatedPicture
    {
        private readonly H264PictureStructure current;

        public H264ColocatedPicture(H264ReferencePicture reference, H264PictureStructure current, H264PictureOrder order)
        {
            this.current = current;
            Picture = reference.Picture;
            if (reference.Companion is H264Picture bottom)
            {
                long topDistance = Math.Abs((long)reference.Order.Top - order.Value);
                long bottomDistance = Math.Abs((long)reference.Order.Bottom - order.Value);
                if (bottomDistance <= topDistance)
                {
                    Picture = bottom;
                }
            }
        }

        public H264Picture Picture { get; }

        public int MapRow(int y) => (current, Picture.Structure) switch
        {
            (H264PictureStructure.Frame, H264PictureStructure.Frame) or
            (H264PictureStructure.TopField or H264PictureStructure.BottomField,
                H264PictureStructure.TopField or H264PictureStructure.BottomField) => y,
            (H264PictureStructure.Frame, H264PictureStructure.TopField or H264PictureStructure.BottomField) => y / 8 * 4,
            (H264PictureStructure.TopField or H264PictureStructure.BottomField, H264PictureStructure.Frame) => y * 2,
            _ => throw new InvalidDataException("Invalid H.264 co-located picture structure.")
        };

        public int MapVerticalMotion(int value) => (current, Picture.Structure) switch
        {
            (H264PictureStructure.Frame, H264PictureStructure.Frame) or
            (H264PictureStructure.TopField or H264PictureStructure.BottomField,
                H264PictureStructure.TopField or H264PictureStructure.BottomField) => value,
            (H264PictureStructure.Frame, H264PictureStructure.TopField or H264PictureStructure.BottomField) => value * 2,
            (H264PictureStructure.TopField or H264PictureStructure.BottomField, H264PictureStructure.Frame) => value / 2,
            _ => throw new InvalidDataException("Invalid H.264 co-located motion structure.")
        };

        public H264PictureIdentity MapIdentity(H264PictureIdentity identity) => (current, Picture.Structure) switch
        {
            (H264PictureStructure.Frame, H264PictureStructure.Frame) or
            (H264PictureStructure.TopField or H264PictureStructure.BottomField,
                H264PictureStructure.TopField or H264PictureStructure.BottomField) => identity,
            (H264PictureStructure.Frame, H264PictureStructure.TopField or H264PictureStructure.BottomField) => identity.Frame,
            (H264PictureStructure.TopField or H264PictureStructure.BottomField, H264PictureStructure.Frame) => identity.ForStructure(current),
            _ => throw new InvalidDataException("Invalid H.264 co-located reference structure.")
        };
    }
}
