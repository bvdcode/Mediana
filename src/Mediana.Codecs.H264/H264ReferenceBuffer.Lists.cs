namespace Mediana.Codecs.H264
{
    internal partial class H264ReferenceBuffer
    {
        public IReadOnlyList<H264ReferencePicture?> GetList0(H264SliceHeader header)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateSequence(header);
            if (header.Kind == H264SliceKind.I)
            {
                return [];
            }
            if (header.Kind != H264SliceKind.P)
            {
                throw new InvalidDataException("Invalid active H.264 P reference list.");
            }
            int maximum = 1 << header.Sequence.FrameNumberBits;
            List<H264ReferencePicture?> list = references.Where(value => value.LongTermIndex is null)
                .OrderByDescending(value => value.PictureNumber(header.FrameNumber, maximum))
                .Concat(references.Where(value => value.LongTermIndex is not null).OrderBy(value => value.LongTermIndex))
                .Cast<H264ReferencePicture?>().ToList();
            return ModifyList(list, header, header.ActiveReferencesL0, header.ReferenceModifications);
        }

        public H264BidirectionalReferences GetBidirectionalLists(H264SliceHeader header, H264PictureOrder order)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateSequence(header);
            if (header.Kind != H264SliceKind.B || references.Count == 0)
            {
                throw new InvalidDataException("Invalid H.264 B reference lists.");
            }
            H264ReferencePicture[] shortTerm = references.Where(value => value.LongTermIndex is null).ToArray();
            H264ReferencePicture[] longTerm = references.Where(value => value.LongTermIndex is not null)
                .OrderBy(value => value.LongTermIndex).ToArray();
            List<H264ReferencePicture?> l0 = shortTerm.Where(value => value.Order.Value < order.Value)
                .OrderByDescending(value => value.Order.Value)
                .Concat(shortTerm.Where(value => value.Order.Value >= order.Value).OrderBy(value => value.Order.Value))
                .Concat(longTerm).Cast<H264ReferencePicture?>().ToList();
            List<H264ReferencePicture?> l1 = shortTerm.Where(value => value.Order.Value > order.Value)
                .OrderBy(value => value.Order.Value)
                .Concat(shortTerm.Where(value => value.Order.Value <= order.Value).OrderByDescending(value => value.Order.Value))
                .Concat(longTerm).Cast<H264ReferencePicture?>().ToList();
            if (l1.Count > 1 && l0.SequenceEqual(l1))
            {
                (l1[0], l1[1]) = (l1[1], l1[0]);
            }
            return new H264BidirectionalReferences(
                ModifyList(l0, header, header.ActiveReferencesL0, header.ReferenceModifications),
                ModifyList(l1, header, header.ActiveReferencesL1, header.ReferenceModificationsL1));
        }

        private IReadOnlyList<H264ReferencePicture?> ModifyList(List<H264ReferencePicture?> list,
            H264SliceHeader header, int activeCount, IReadOnlyList<H264ReferenceModification> modifications)
        {
            if (activeCount is < 1 or > 32 || modifications.Count > activeCount)
            {
                throw new InvalidDataException("Invalid H.264 active reference count or list modifications.");
            }
            ResizeList(list, activeCount);
            int maximum = 1 << header.Sequence.FrameNumberBits;
            int predicted = header.FrameNumber;
            for (int index = 0; index < modifications.Count; index++)
            {
                H264ReferenceModification modification = modifications[index];
                H264ReferencePicture selected;
                switch (modification.Operation)
                {
                    case 0:
                    case 1:
                        if (modification.Value < 0 || modification.Value >= maximum)
                        {
                            throw new InvalidDataException("Invalid H.264 reference picture number difference.");
                        }
                        int difference = modification.Value + 1;
                        predicted = modification.Operation == 0 ? (predicted - difference + maximum) % maximum
                            : (predicted + difference) % maximum;
                        int number = predicted > header.FrameNumber ? predicted - maximum : predicted;
                        selected = FindShort(references, number, header.FrameNumber, maximum);
                        break;
                    case 2:
                        selected = FindLong(references, modification.Value);
                        break;
                    default:
                        throw new InvalidDataException("Invalid H.264 reference list modification.");
                }
                list.Insert(index, selected);
                for (int position = list.Count - 1; position > index; position--)
                {
                    if (ReferenceEquals(list[position]?.Picture, selected.Picture))
                    {
                        list.RemoveAt(position);
                    }
                }
                ResizeList(list, activeCount);
            }
            return list.AsReadOnly();
        }

        private static void ResizeList(List<H264ReferencePicture?> list, int count)
        {
            if (list.Count > count)
            {
                list.RemoveRange(count, list.Count - count);
            }
            while (list.Count < count)
            {
                list.Add(null);
            }
        }
    }
}
