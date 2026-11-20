// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264PictureIdentity
    {
        private readonly H264PictureIdentity? frame;
        private H264PictureIdentity? top;
        private H264PictureIdentity? bottom;

        public H264PictureIdentity()
        {
        }

        private H264PictureIdentity(H264PictureIdentity frame, H264PictureStructure structure)
        {
            this.frame = frame;
            Structure = structure;
        }

        public H264PictureStructure Structure { get; } = H264PictureStructure.Frame;
        public H264PictureIdentity Frame => frame ?? this;

        public H264PictureIdentity ForStructure(H264PictureStructure structure) => structure switch
        {
            H264PictureStructure.Frame => Frame,
            H264PictureStructure.TopField => Field(ref Frame.top, H264PictureStructure.TopField),
            H264PictureStructure.BottomField => Field(ref Frame.bottom, H264PictureStructure.BottomField),
            _ => throw new ArgumentOutOfRangeException(nameof(structure))
        };

        private H264PictureIdentity Field(ref H264PictureIdentity? slot, H264PictureStructure structure)
        {
            H264PictureIdentity? existing = Volatile.Read(ref slot);
            if (existing is not null) { return existing; }
            H264PictureIdentity created = new(Frame, structure);
            return Interlocked.CompareExchange(ref slot, created, null) ?? created;
        }
    }
}
