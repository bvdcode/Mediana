// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal ref partial struct H264DeblockingContext
    {
        private readonly H264Picture picture;
        private readonly IReadOnlyList<H264ReferencePicture?>? referencesL0;
        private readonly IReadOnlyList<H264ReferencePicture?>? referencesL1;
        private readonly H264PictureIdentity? singleReference;
        private readonly ReadOnlySpan<byte> modes;
        private readonly ReadOnlySpan<byte> counts;
        private readonly ReadOnlySpan<uint> packedResiduals;
        private readonly ReadOnlySpan<H264MacroblockFlags> flags;
        private readonly ReadOnlySpan<byte> indicesL0;
        private readonly ReadOnlySpan<byte> indicesL1;
        private readonly H264StoredMotion motionL0;
        private readonly H264StoredMotion motionL1;
        private readonly int stride;
        private readonly int columns;
        private bool? uniformPrediction;
        private bool uniformValidated;
        private int address;
        private uint currentResiduals;
        private uint verticalResidualEdges;
        private uint horizontalResidualEdges;
        private H264PictureReferences? currentReferences;
        private H264PictureReferences? leftReferences;
        private H264PictureReferences? topReferences;

        public H264DeblockingContext(H264Picture picture, IReadOnlyList<H264ReferencePicture?>? referencesL0,
            IReadOnlyList<H264ReferencePicture?>? referencesL1, H264PictureIdentity? singleReference = null)
        {
            this.picture = picture;
            this.referencesL0 = referencesL0;
            this.referencesL1 = referencesL1;
            this.singleReference = singleReference;
            modes = picture.Modes;
            packedResiduals = MemoryMarshal.Cast<byte, uint>(picture.PackedResidualStorage.Span);
            counts = packedResiduals.IsEmpty ? picture.Counts(0) : [];
            flags = picture.MacroblockFlags;
            indicesL0 = picture.ReferenceIndices(H264ReferenceList.L0);
            motionL0 = picture.MotionVectors(H264ReferenceList.L0);
            if (picture.HasBidirectionalMotion)
            {
                indicesL1 = picture.ReferenceIndices(H264ReferenceList.L1);
                motionL1 = picture.MotionVectors(H264ReferenceList.L1);
            }
            stride = picture.Width / 4;
            columns = picture.Sequence.WidthInMbs;
        }

        public void BeginMacroblock(int macroblock)
        {
            address = macroblock;
            if (!packedResiduals.IsEmpty)
            {
                currentResiduals = packedResiduals[macroblock] & H264CodedBlockFlags.Luma;
                uint left = macroblock % columns == 0 ? 0 : packedResiduals[macroblock - 1] & H264CodedBlockFlags.Luma;
                uint top = macroblock < columns ? 0 : packedResiduals[macroblock - columns] & H264CodedBlockFlags.Luma;
                verticalResidualEdges = currentResiduals | H264CodedBlockFlags.InteriorLeft(currentResiduals) |
                    H264CodedBlockFlags.LeftBorder(left);
                horizontalResidualEdges = currentResiduals | H264CodedBlockFlags.InteriorTop(currentResiduals) |
                    H264CodedBlockFlags.TopBorder(top);
            }
            currentReferences = null;
            leftReferences = null;
            topReferences = null;
        }

        public int Strength(int x, int y, bool vertical, bool macroblockEdge) =>
            Strength(x, y, vertical, macroblockEdge, false);

        private int Strength(int x, int y, bool vertical, bool macroblockEdge, bool reuseUniformPrediction)
        {
            int current = y / 4 * stride + x / 4;
            int previous = current - (vertical ? 1 : stride);
            if (modes[previous] != H264Picture.InterMode || modes[current] != H264Picture.InterMode)
            {
                return macroblockEdge && (vertical || !picture.Layout.IsField) ? 4 : 3;
            }
            bool previousMacroblock = ((vertical ? x : y) & 15) == 0;
            int previousAddress = previousMacroblock ? address - (vertical ? 1 : columns) : address;
            bool residual = packedResiduals.IsEmpty
                ? HasResidual(x - (vertical ? 1 : 0), y - (vertical ? 0 : 1), previous, previousAddress)
                    || HasResidual(x, y, current, address)
                : (((vertical ? verticalResidualEdges : horizontalResidualEdges) >> H264CodedBlockFlags.LumaIndex(x, y)) & 1u) != 0;
            if (residual)
            {
                return 2;
            }
            if (reuseUniformPrediction && !previousMacroblock && UniformPrediction(previous, vertical))
            {
                return 0;
            }
            H264PictureIdentity? p0 = Resolve(H264ReferenceList.L0, previous, true, vertical, previousMacroblock);
            H264PictureIdentity? q0 = Resolve(H264ReferenceList.L0, current, false, vertical, previousMacroblock);
            H264PictureIdentity? p1 = picture.HasBidirectionalMotion
                ? Resolve(H264ReferenceList.L1, previous, true, vertical, previousMacroblock) : null;
            H264PictureIdentity? q1 = picture.HasBidirectionalMotion
                ? Resolve(H264ReferenceList.L1, current, false, vertical, previousMacroblock) : null;
            return PredictionStrength(previous, current, p0, q0, p1, q1, picture.Layout.IsField ? 2 : 4);
        }

        private readonly int PredictionStrength(int previous, int current, H264PictureIdentity? p0, H264PictureIdentity? q0,
            H264PictureIdentity? p1, H264PictureIdentity? q1, int verticalLimit)
        {
            if ((p0 is null && p1 is null) || (q0 is null && q1 is null))
            {
                throw new InvalidDataException("The H.264 inter edge has no prediction reference.");
            }
            if (ReferenceEquals(p0, q0) && ReferenceEquals(p1, q1)
                && (p0 is null || motionL0.Near(motionL0, previous, current, verticalLimit))
                && (p1 is null || motionL1.Near(motionL1, previous, current, verticalLimit)))
            {
                return 0;
            }
            return ReferenceEquals(p0, q1) && ReferenceEquals(p1, q0)
                && (p0 is null || motionL0.Near(motionL1, previous, current, verticalLimit))
                && (p1 is null || motionL1.Near(motionL0, previous, current, verticalLimit)) ? 0 : 1;
        }

        private readonly bool HasResidual(int x, int y, int position, int macroblock)
        {
            if (!flags[macroblock].HasFlag(H264MacroblockFlags.Transform8x8))
            {
                return counts[position] != 0;
            }
            int first = y / 8 * 2 * stride + x / 8 * 2;
            return counts[first] != 0 || counts[first + 1] != 0 || counts[first + stride] != 0 || counts[first + stride + 1] != 0;
        }

        private H264PictureIdentity? Resolve(H264ReferenceList list, int index,
            bool previous, bool vertical, bool macroblockEdge)
        {
            ReadOnlySpan<byte> indices;
            IReadOnlyList<H264ReferencePicture?>? references;
            switch (list)
            {
                case H264ReferenceList.L0:
                    indices = indicesL0;
                    references = referencesL0;
                    break;
                case H264ReferenceList.L1:
                    indices = indicesL1;
                    references = referencesL1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(list));
            }
            int referenceIndex = indices[index];
            if (referenceIndex == byte.MaxValue)
            {
                return null;
            }
            if (singleReference is not null)
            {
                if (list != H264ReferenceList.L0 || referenceIndex != 0)
                {
                    throw new InvalidDataException("The H.264 edge does not use the single prediction reference.");
                }
                return singleReference;
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
                identity = reference.Identity;
            }
            else
            {
                throw new InvalidDataException("The H.264 edge uses an unavailable reference picture.");
            }
            return identity;
        }
    }
}
