// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264SliceHeaders
    {
        public static IReadOnlyList<H264SliceRegion> Read(H264AccessUnit unit, H264DecoderOptions options,
            CancellationToken cancellationToken)
        {
            List<H264SliceRegion> slices = [];
            H264SliceHeader? first = null;
            H264SliceHeader? previous = null;
            H264SliceKind? requiredKind = null;
            bool mixedKinds = false;
            foreach (ReadOnlyMemory<byte> nal in unit.Slices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                H264BitReader bits = H264Nal.Payload(nal.Span, H264Nal.Type(nal.Span));
                H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, unit.Configuration, nal.Span[0], options);
                if (first is not null)
                {
                    ValidatePicture(first, header);
                    mixedKinds |= first.Kind != header.Kind;
                }
                if (header.AllSlicesSameKind)
                {
                    requiredKind = header.Kind;
                }
                if (requiredKind is not null && (mixedKinds || requiredKind != header.Kind))
                {
                    throw new InvalidDataException("The H.264 picture violates its same-slice-type declaration.");
                }
                first ??= header;
                if (previous is not null)
                {
                    slices.Add(new H264SliceRegion(previous, header.FirstMacroblock));
                }
                previous = header;
            }
            if (previous is not null)
            {
                slices.Add(new H264SliceRegion(previous, checked(previous.Sequence.WidthInMbs * previous.Sequence.HeightInMapUnits)));
            }
            return slices;
        }

        private static void ValidatePicture(H264SliceHeader first, H264SliceHeader next)
        {
            if (first.FrameNumber != next.FrameNumber || first.Picture.Id != next.Picture.Id || first.IsIdr != next.IsIdr ||
                first.IdrPictureId != next.IdrPictureId || (first.ReferenceIndicator == 0) != (next.ReferenceIndicator == 0) ||
                first.PictureOrderLsb != next.PictureOrderLsb || first.DeltaPictureOrderBottom != next.DeltaPictureOrderBottom ||
                first.DeltaPictureOrder0 != next.DeltaPictureOrder0 || first.DeltaPictureOrder1 != next.DeltaPictureOrder1)
            {
                throw new InvalidDataException("The AVC packet contains slices from different primary coded pictures.");
            }
            if (first.NoOutputOfPriorPictures != next.NoOutputOfPriorPictures || first.LongTermReference != next.LongTermReference ||
                first.AdaptiveMarking != next.AdaptiveMarking || !first.MemoryOperations.SequenceEqual(next.MemoryOperations))
            {
                throw new InvalidDataException("H.264 reference marking must agree across all slices of a picture.");
            }
        }
    }
}
