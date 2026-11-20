// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal ref partial struct H264DeblockingContext
    {
        public readonly int AdaptiveStrength(H264NeighbourLocation previous, H264NeighbourLocation current, bool vertical)
        {
            (int px, int py) = picture.Grid.Position(previous.Address);
            (int qx, int qy) = picture.Grid.Position(current.Address);
            px += previous.X;
            py += previous.Y;
            qx += current.X;
            qy += current.Y;
            int p = picture.BlockIndex(0, px, py);
            int q = picture.BlockIndex(0, qx, qy);
            bool pField = picture.Grid.IsField(previous.Address);
            bool qField = picture.Grid.IsField(current.Address);
            if (modes[p] != H264Picture.InterMode || modes[q] != H264Picture.InterMode)
            {
                return previous.Address != current.Address && (vertical || !pField && !qField) ? 4 : 3;
            }
            if (HasResidual(px, py, p, previous.Address) || HasResidual(qx, qy, q, current.Address))
            {
                return 2;
            }
            if (pField != qField)
            {
                return 1;
            }
            H264PictureIdentity? p0 = AdaptiveReference(H264ReferenceList.L0, p, previous.Address);
            H264PictureIdentity? q0 = AdaptiveReference(H264ReferenceList.L0, q, current.Address);
            H264PictureIdentity? p1 = picture.HasBidirectionalMotion ? AdaptiveReference(H264ReferenceList.L1, p, previous.Address) : null;
            H264PictureIdentity? q1 = picture.HasBidirectionalMotion ? AdaptiveReference(H264ReferenceList.L1, q, current.Address) : null;
            return PredictionStrength(p, q, p0, q0, p1, q1, qField ? 2 : 4);
        }

        private readonly H264PictureIdentity? AdaptiveReference(H264ReferenceList list, int position, int macroblock)
        {
            (int index, IReadOnlyList<H264ReferencePicture?>? references) = list switch
            {
                H264ReferenceList.L0 => (indicesL0[position], referencesL0),
                H264ReferenceList.L1 => (indicesL1[position], referencesL1),
                _ => throw new ArgumentOutOfRangeException(nameof(list))
            };
            if (index == byte.MaxValue)
            {
                return null;
            }
            if (references is null)
            {
                return picture.ReferenceIdentity(list, index, macroblock);
            }
            bool field = picture.Grid.IsField(macroblock);
            int frameIndex = field ? index / 2 : index;
            if ((uint)frameIndex >= (uint)references.Count || references[frameIndex] is not H264ReferencePicture reference)
            {
                throw new InvalidDataException("An adaptive deblocking edge refers to an unavailable picture.");
            }
            if (!field)
            {
                return reference.Identity;
            }
            H264PictureStructure structure = ((macroblock + index) & 1) switch
            {
                0 => H264PictureStructure.TopField,
                1 => H264PictureStructure.BottomField,
                _ => throw new InvalidOperationException("Invalid reference field parity.")
            };
            return reference.Identity.ForStructure(structure);
        }
    }
}
