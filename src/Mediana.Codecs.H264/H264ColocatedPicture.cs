// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264ColocatedPicture
    {
        private readonly H264Picture target;
        private readonly H264ReferencePicture reference;
        private readonly H264MacroblockPredictionContext context;
        private readonly H264PictureOrder order;
        private readonly H264Picture picture;

        public H264ColocatedPicture(H264ReferencePicture reference, H264Picture target,
            H264MacroblockPredictionContext context, H264PictureOrder order)
        {
            this.reference = reference;
            this.target = target;
            this.context = context;
            this.order = order;
            picture = reference.Picture;
            if (reference.Companion is H264Picture bottom && SelectBottom())
            {
                picture = bottom;
            }
        }

        public bool SharesFrameGrid => target.Structure == H264PictureStructure.Frame && picture.Structure == H264PictureStructure.Frame
            && !target.Layout.MacroblockAdaptiveFrameField && !picture.Layout.MacroblockAdaptiveFrameField
            && target.Width == picture.Width && target.Height == picture.Height;

        public H264ColocatedBlock MapBlock(int x, int y)
        {
            if (picture.Layout.MacroblockAdaptiveFrameField)
            {
                return MapAdaptiveBlock(x, y);
            }
            int currentY = context.AdaptiveField ? y / 32 * 16 + y % 16 : y;
            int mappedY = (context.Structure, picture.Structure) switch
            {
                (H264PictureStructure.Frame, H264PictureStructure.Frame) or
                (H264PictureStructure.TopField or H264PictureStructure.BottomField,
                    H264PictureStructure.TopField or H264PictureStructure.BottomField) => currentY,
                (H264PictureStructure.Frame, H264PictureStructure.TopField or H264PictureStructure.BottomField) => currentY / 8 * 4,
                (H264PictureStructure.TopField or H264PictureStructure.BottomField, H264PictureStructure.Frame) => currentY * 2,
                _ => throw new InvalidDataException("Invalid H.264 co-located picture structure.")
            };
            int address = picture.Grid.Address(x, mappedY);
            return new(picture, address, x, mappedY, context.Structure, picture.Structure != H264PictureStructure.Frame);
        }

        private H264ColocatedBlock MapAdaptiveBlock(int x, int y)
        {
            int current = target.Grid.Address(x, y);
            int localY = y % 16;
            int address;
            int mappedY;
            if (target.Structure != H264PictureStructure.Frame)
            {
                int pair = current * 2;
                bool sourceField = picture.Grid.IsField(pair);
                int parity = target.Structure switch
                {
                    H264PictureStructure.TopField => 0,
                    H264PictureStructure.BottomField => 1,
                    _ => throw new InvalidDataException("Invalid H.264 co-located field parity.")
                };
                address = pair + (sourceField ? parity : localY / 8);
                mappedY = sourceField ? localY : localY * 2 % 16;
            }
            else
            {
                if (!target.Layout.MacroblockAdaptiveFrameField)
                {
                    throw new InvalidDataException("H.264 frame and adaptive-frame coding must be separated by an IDR picture.");
                }
                int pair = current & ~1;
                bool sourceField = picture.Grid.IsField(pair);
                bool currentField = context.AdaptiveField;
                (address, mappedY) = (currentField, sourceField) switch
                {
                    (false, false) or (true, true) => (current, localY),
                    (false, true) => (pair + (SelectBottom() ? 1 : 0), 8 * (current & 1) + 4 * (localY / 8)),
                    (true, false) => (pair + localY / 8, localY * 2 % 16)
                };
            }
            (int originX, int originY) = picture.Grid.Position(address);
            return new(picture, address, originX + x % 16, originY + mappedY, context.Structure, picture.Grid.IsField(address));
        }

        private bool SelectBottom()
        {
            if (context.AdaptiveField)
            {
                return context.Structure == H264PictureStructure.BottomField;
            }
            long topDistance = Math.Abs((long)reference.Order.Top - order.Value);
            long bottomDistance = Math.Abs((long)reference.Order.Bottom - order.Value);
            return bottomDistance <= topDistance;
        }
    }
}
