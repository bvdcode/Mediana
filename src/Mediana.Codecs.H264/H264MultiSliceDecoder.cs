// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264MultiSliceDecoder
    {
        public static H264Picture Decode(IReadOnlyList<H264SliceRegion> slices,
            H264ReferenceBuffer references, H264PictureOrder order, MediaBufferAllocator allocator,
            CancellationToken cancellationToken)
        {
            H264SliceKind storageKind = slices.Any(slice => slice.Header.Kind == H264SliceKind.B) ? H264SliceKind.B : H264SliceKind.P;
            H264Picture picture = H264Picture.CreateForCoding(slices[0].Header.Sequence, allocator, storageKind, slices[0].Header.Structure);
            bool complete = false;
            try
            {
                H264BidirectionalReferences[] sliceReferences = new H264BidirectionalReferences[slices.Count];
                for (int index = 0; index < slices.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    H264SliceHeader header = slices[index].Header;
                    H264BidirectionalReferences lists = header.Kind switch
                    {
                        H264SliceKind.B => references.GetBidirectionalLists(header, order),
                        H264SliceKind.I or H264SliceKind.P => new(references.GetList0(header), []),
                        _ => throw new NotSupportedException("H.264 reconstruction supports I, P and B slices.")
                    };
                    sliceReferences[index] = lists;
                    picture.CaptureReferences(header.FirstMacroblock, lists.L0, lists.L1);
                }
                for (int index = 0; index < slices.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    H264SliceRegion region = slices[index];
                    H264SliceHeader header = region.Header;
                    H264BitReader bits = region.CreatePayloadReader();
                    H264BidirectionalReferences lists = sliceReferences[index];
                    picture.FirstMacroblock = header.FirstMacroblock;
                    if (header.Picture.Cabac)
                    {
                        H264CabacPictureDecoder.DecodeSlice(ref bits, header, lists, order, picture, region.EndMacroblock, allocator, cancellationToken);
                    }
                    else if (header.Kind == H264SliceKind.B)
                    {
                        H264BidirectionalPictureDecoder.DecodeSlice(ref bits, header, lists, order, picture, region.EndMacroblock, cancellationToken);
                    }
                    else
                    {
                        H264PictureDecoder.DecodeSlice(ref bits, header, lists.L0, picture, region.EndMacroblock, cancellationToken);
                    }
                }
                picture.FirstMacroblock = 0;
                H264Deblocking.Apply(picture, slices, cancellationToken);
                complete = true;
                return picture;
            }
            finally
            {
                if (!complete)
                {
                    picture.Dispose();
                }
            }
        }
    }
}
