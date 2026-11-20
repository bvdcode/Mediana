// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264ReferenceBuffer
    {
        private IReadOnlyList<H264ReferencePicture?> GetFieldList0(H264SliceHeader header)
        {
            int maximum = 1 << header.Sequence.FrameNumberBits;
            IEnumerable<IGrouping<H264PictureIdentity, H264ReferencePicture>> shortTerm = references
                .Where(value => value.LongTermIndex is null).GroupBy(value => value.FrameIdentity)
                .OrderByDescending(group => group.First().PictureNumber(header.FrameNumber, maximum));
            IEnumerable<IGrouping<H264PictureIdentity, H264ReferencePicture>> longTerm = references
                .Where(value => value.LongTermIndex is not null).GroupBy(value => value.FrameIdentity)
                .OrderBy(group => group.First().LongTermIndex);
            List<H264ReferencePicture?> list = [];
            AppendFields(list, shortTerm, header.Structure);
            AppendFields(list, longTerm, header.Structure);
            return ModifyList(list, header, header.ActiveReferencesL0, header.ReferenceModifications);
        }

        private H264BidirectionalReferences GetFieldBidirectionalLists(H264SliceHeader header, H264PictureOrder order)
        {
            IGrouping<H264PictureIdentity, H264ReferencePicture>[] shortTerm = references
                .Where(value => value.LongTermIndex is null).GroupBy(value => value.FrameIdentity).ToArray();
            IGrouping<H264PictureIdentity, H264ReferencePicture>[] longTerm = references
                .Where(value => value.LongTermIndex is not null).GroupBy(value => value.FrameIdentity)
                .OrderBy(group => group.First().LongTermIndex).ToArray();
            IGrouping<H264PictureIdentity, H264ReferencePicture>[] earlier = shortTerm
                .Where(group => group.Min(value => value.Order.Value) <= order.Value)
                .OrderByDescending(group => group.Min(value => value.Order.Value)).ToArray();
            IGrouping<H264PictureIdentity, H264ReferencePicture>[] later = shortTerm
                .Where(group => group.Min(value => value.Order.Value) > order.Value)
                .OrderBy(group => group.Min(value => value.Order.Value)).ToArray();
            List<H264ReferencePicture?> l0 = [];
            List<H264ReferencePicture?> l1 = [];
            AppendFields(l0, earlier.Concat(later), header.Structure);
            AppendFields(l1, later.Concat(earlier), header.Structure);
            AppendFields(l0, longTerm, header.Structure);
            AppendFields(l1, longTerm, header.Structure);
            if (l1.Count > 1 && l0.SequenceEqual(l1))
            {
                (l1[0], l1[1]) = (l1[1], l1[0]);
            }
            return new H264BidirectionalReferences(
                ModifyList(l0, header, header.ActiveReferencesL0, header.ReferenceModifications),
                ModifyList(l1, header, header.ActiveReferencesL1, header.ReferenceModificationsL1));
        }

        private static void AppendFields(List<H264ReferencePicture?> destination,
            IEnumerable<IGrouping<H264PictureIdentity, H264ReferencePicture>> groups, H264PictureStructure current)
        {
            List<H264ReferencePicture> ordered = groups.SelectMany(group => group).ToList();
            SeparateFields(ordered);
            int same = 0;
            int opposite = 0;
            while (same < ordered.Count || opposite < ordered.Count)
            {
                while (same < ordered.Count && ordered[same].Structure != current)
                {
                    same++;
                }
                if (same < ordered.Count)
                {
                    destination.Add(ordered[same++]);
                }
                while (opposite < ordered.Count && ordered[opposite].Structure == current)
                {
                    opposite++;
                }
                if (opposite < ordered.Count)
                {
                    destination.Add(ordered[opposite++]);
                }
            }
        }
    }
}
