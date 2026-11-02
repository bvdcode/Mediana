// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal ref struct H264DeblockingContext
    {
        private readonly H264Picture picture;
        private readonly IReadOnlyList<H264ReferencePicture?>? referencesL0;
        private readonly IReadOnlyList<H264ReferencePicture?>? referencesL1;
        private readonly ReadOnlySpan<byte> modes;
        private readonly ReadOnlySpan<byte> counts;
        private readonly ReadOnlySpan<byte> transforms;
        private readonly ReadOnlySpan<byte> indicesL0;
        private readonly ReadOnlySpan<byte> indicesL1;
        private readonly ReadOnlySpan<int> motionXL0;
        private readonly ReadOnlySpan<int> motionYL0;
        private readonly ReadOnlySpan<int> motionXL1;
        private readonly ReadOnlySpan<int> motionYL1;
        private readonly int stride;
        private readonly int columns;
        private int address;
        private H264PictureReferences? currentReferences;
        private H264PictureReferences? leftReferences;
        private H264PictureReferences? topReferences;

        public H264DeblockingContext(H264Picture picture, IReadOnlyList<H264ReferencePicture?>? referencesL0,
            IReadOnlyList<H264ReferencePicture?>? referencesL1)
        {
            this.picture = picture;
            this.referencesL0 = referencesL0;
            this.referencesL1 = referencesL1;
            modes = picture.Modes;
            counts = picture.Counts(0);
            transforms = picture.Transforms8x8;
            indicesL0 = picture.ReferenceIndices(H264ReferenceList.L0);
            motionXL0 = picture.MotionX(H264ReferenceList.L0);
            motionYL0 = picture.MotionY(H264ReferenceList.L0);
            if (picture.HasBidirectionalMotion)
            {
                indicesL1 = picture.ReferenceIndices(H264ReferenceList.L1);
                motionXL1 = picture.MotionX(H264ReferenceList.L1);
                motionYL1 = picture.MotionY(H264ReferenceList.L1);
            }
            stride = picture.Width / 4;
            columns = picture.Sequence.WidthInMbs;
        }

        public void BeginMacroblock(int macroblock)
        {
            address = macroblock;
            currentReferences = null;
            leftReferences = null;
            topReferences = null;
        }

        public int Strength(int x, int y, bool vertical, bool macroblockEdge)
        {
            int current = y / 4 * stride + x / 4;
            int previous = current - (vertical ? 1 : stride);
            if (modes[previous] != H264Picture.InterMode || modes[current] != H264Picture.InterMode)
            {
                return macroblockEdge ? 4 : 3;
            }
            bool previousMacroblock = ((vertical ? x : y) & 15) == 0;
            int previousAddress = previousMacroblock ? address - (vertical ? 1 : columns) : address;
            if (HasResidual(x - (vertical ? 1 : 0), y - (vertical ? 0 : 1), previous, previousAddress)
                || HasResidual(x, y, current, address))
            {
                return 2;
            }
            (H264PictureIdentity? Picture, int X, int Y) p0 = Resolve(H264ReferenceList.L0, previous, true, vertical, previousMacroblock);
            (H264PictureIdentity? Picture, int X, int Y) q0 = Resolve(H264ReferenceList.L0, current, false, vertical, previousMacroblock);
            (H264PictureIdentity? Picture, int X, int Y) p1 = picture.HasBidirectionalMotion
                ? Resolve(H264ReferenceList.L1, previous, true, vertical, previousMacroblock) : (null, 0, 0);
            (H264PictureIdentity? Picture, int X, int Y) q1 = picture.HasBidirectionalMotion
                ? Resolve(H264ReferenceList.L1, current, false, vertical, previousMacroblock) : (null, 0, 0);
            return H264DeblockingMotion.Strength(p0, q0, p1, q1);
        }

        private readonly bool HasResidual(int x, int y, int position, int macroblock)
        {
            if (transforms[macroblock] == 0)
            {
                return counts[position] != 0;
            }
            int first = y / 8 * 2 * stride + x / 8 * 2;
            return counts[first] != 0 || counts[first + 1] != 0 || counts[first + stride] != 0 || counts[first + stride + 1] != 0;
        }

        private (H264PictureIdentity? Picture, int X, int Y) Resolve(H264ReferenceList list, int index,
            bool previous, bool vertical, bool macroblockEdge)
        {
            ReadOnlySpan<byte> indices;
            ReadOnlySpan<int> motionX;
            ReadOnlySpan<int> motionY;
            IReadOnlyList<H264ReferencePicture?>? references;
            switch (list)
            {
                case H264ReferenceList.L0:
                    indices = indicesL0;
                    motionX = motionXL0;
                    motionY = motionYL0;
                    references = referencesL0;
                    break;
                case H264ReferenceList.L1:
                    indices = indicesL1;
                    motionX = motionXL1;
                    motionY = motionYL1;
                    references = referencesL1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(list));
            }
            int referenceIndex = indices[index];
            if (referenceIndex == byte.MaxValue)
            {
                return (null, 0, 0);
            }
            H264PictureIdentity identity;
            if (references is null)
            {
                H264PictureReferences slice = !previous || !macroblockEdge
                    ? currentReferences ??= picture.ReferenceLists(address)
                    : vertical ? leftReferences ??= picture.ReferenceLists(address - 1)
                    : topReferences ??= picture.ReferenceLists(address - columns);
                H264PictureIdentity?[] identities = list switch
                {
                    H264ReferenceList.L0 => slice.L0,
                    H264ReferenceList.L1 => slice.L1,
                    _ => throw new ArgumentOutOfRangeException(nameof(list))
                };
                if ((uint)referenceIndex >= (uint)identities.Length || identities[referenceIndex] is not H264PictureIdentity captured)
                {
                    throw new InvalidDataException("The H.264 edge uses an unavailable reference picture.");
                }
                identity = captured;
            }
            else if (referenceIndex < references.Count && references[referenceIndex] is H264ReferencePicture reference)
            {
                identity = reference.Picture.Identity;
            }
            else
            {
                throw new InvalidDataException("The H.264 edge uses an unavailable reference picture.");
            }
            return (identity, motionX[index], motionY[index]);
        }
    }
}
