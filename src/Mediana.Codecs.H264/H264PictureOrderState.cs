namespace Mediana.Codecs.H264
{
    internal record H264PictureOrderState
    {
        private H264SequenceParameterSet? Sequence { get; init; }
        private int PreviousFrameNumber { get; init; }
        private int PreviousReferenceFrameNumber { get; init; }
        private int FrameNumberOffset { get; init; }
        private int ReferenceOrderMsb { get; init; }
        private int ReferenceOrderLsb { get; init; }
        public H264PictureOrder Current { get; private init; }

        public H264PictureOrderState Advance(H264SliceHeader header)
        {
            H264SequenceParameterSet sequence = header.Sequence;
            if (!header.IsIdr && Sequence != sequence)
            {
                throw new NotSupportedException("H.264 picture ordering must begin with an IDR picture.");
            }
            int maximumNumber = 1 << sequence.FrameNumberBits;
            if (!header.IsIdr && header.FrameNumber != (PreviousReferenceFrameNumber + 1) % maximumNumber)
            {
                if (header.FrameNumber == PreviousReferenceFrameNumber || !sequence.FrameNumberGapsAllowed)
                {
                    throw new InvalidDataException("The H.264 frame number repeats a reference frame or indicates missing pictures.");
                }
                throw new NotSupportedException("H.264 non-existing reference pictures from frame number gaps are not implemented.");
            }
            long frameOffset = header.IsIdr || sequence.PictureOrderType == 0 ? 0
                : (long)FrameNumberOffset + (PreviousFrameNumber > header.FrameNumber ? maximumNumber : 0);
            long msb = 0;
            long top;
            long bottom;
            switch (sequence.PictureOrderType)
            {
                case 0:
                    int previousMsb = header.IsIdr ? 0 : ReferenceOrderMsb;
                    int previousLsb = header.IsIdr ? 0 : ReferenceOrderLsb;
                    int maximumLsb = 1 << sequence.PictureOrderBits;
                    msb = previousMsb;
                    if (header.PictureOrderLsb < previousLsb && previousLsb - header.PictureOrderLsb >= maximumLsb / 2)
                    {
                        msb += maximumLsb;
                    }
                    else if (header.PictureOrderLsb > previousLsb && header.PictureOrderLsb - previousLsb > maximumLsb / 2)
                    {
                        msb -= maximumLsb;
                    }
                    top = msb + header.PictureOrderLsb;
                    bottom = top + header.DeltaPictureOrderBottom;
                    break;
                case 1:
                    long absoluteFrame = sequence.PictureOrderCycle.Count == 0 ? 0 : frameOffset + header.FrameNumber;
                    if (header.ReferenceIndicator == 0 && absoluteFrame > 0)
                    {
                        absoluteFrame--;
                    }
                    long expected = 0;
                    if (absoluteFrame > 0)
                    {
                        long delta = 0;
                        for (int index = 0; index < sequence.PictureOrderCycle.Count; index++)
                        {
                            delta += sequence.PictureOrderCycle[index];
                        }
                        expected = (absoluteFrame - 1) / sequence.PictureOrderCycle.Count * delta;
                        for (int index = 0; index <= (absoluteFrame - 1) % sequence.PictureOrderCycle.Count; index++)
                        {
                            expected += sequence.PictureOrderCycle[index];
                        }
                    }
                    if (header.ReferenceIndicator == 0)
                    {
                        expected += sequence.NonReferencePictureOrderOffset;
                    }
                    top = expected + header.DeltaPictureOrder0;
                    bottom = top + sequence.TopToBottomPictureOrderOffset + header.DeltaPictureOrder1;
                    break;
                case 2:
                    top = header.IsIdr ? 0 : 2 * (frameOffset + header.FrameNumber) - (header.ReferenceIndicator == 0 ? 1 : 0);
                    bottom = top;
                    if (!header.IsIdr && top <= Current.Value)
                    {
                        throw new InvalidDataException("H.264 type-two picture order must increase between resets.");
                    }
                    break;
                default:
                    throw new InvalidDataException("Invalid H.264 picture order type.");
            }
            H264PictureOrder order = new(Bounded(top), Bounded(bottom));
            if (header.IsIdr && order.Value != 0)
            {
                throw new InvalidDataException("An H.264 IDR picture must start at picture order zero.");
            }
            bool reset = header.MemoryOperations.Any(operation => operation.Operation == 5);
            if (reset)
            {
                order = new H264PictureOrder(Bounded(top - order.Value), Bounded(bottom - order.Value));
            }
            return new H264PictureOrderState
            {
                Sequence = sequence,
                PreviousFrameNumber = reset ? 0 : header.FrameNumber,
                PreviousReferenceFrameNumber = header.ReferenceIndicator == 0 ? PreviousReferenceFrameNumber : reset ? 0 : header.FrameNumber,
                FrameNumberOffset = reset ? 0 : Bounded(frameOffset),
                ReferenceOrderMsb = header.ReferenceIndicator == 0 ? ReferenceOrderMsb : reset ? 0 : Bounded(msb),
                ReferenceOrderLsb = header.ReferenceIndicator == 0 ? ReferenceOrderLsb : reset ? order.Top : header.PictureOrderLsb,
                Current = order
            };
        }

        private static int Bounded(long value) => value is >= int.MinValue and <= int.MaxValue ? (int)value
            : throw new InvalidDataException("An H.264 picture order value exceeds the signed 32-bit range.");
    }
}
