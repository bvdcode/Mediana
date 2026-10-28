namespace Mediana.Codecs.H264
{
    internal record H264IntraSliceHeader(H264SequenceParameterSet Sequence, H264PictureParameterSet Picture,
        int Qp, bool DeblockingEnabled, int AlphaOffset, int BetaOffset)
    {
        public static H264IntraSliceHeader Read(ref H264BitReader bits, H264Configuration configuration)
        {
            int firstMacroblock = bits.ReadUe();
            int sliceType = bits.ReadUe(9) % 5;
            int pictureId = bits.ReadUe(255);
            if (!configuration.Pictures.TryGetValue(pictureId, out H264PictureParameterSet? picture))
            {
                throw new InvalidDataException("The H.264 slice references an unknown picture parameter set.");
            }
            H264SequenceParameterSet sequence = configuration.Sequences[picture.SequenceId];
            if (firstMacroblock != 0 || sliceType != 2 || !sequence.FrameMbsOnly || picture.Cabac ||
                sequence.ChromaFormat != 1 || sequence.SeparateColourPlane || sequence.LumaBitDepth != 8 ||
                sequence.ChromaBitDepth != 8 || sequence.ScalingMatrix || sequence.TransformBypass || picture.Transform8x8)
            {
                throw new NotSupportedException("Intra reconstruction currently requires one progressive 8-bit 4:2:0 CAVLC I slice with default scaling.");
            }
            if (bits.ReadBits(sequence.FrameNumberBits) != 0)
            {
                throw new InvalidDataException("An H.264 IDR picture must have frame_num equal to zero.");
            }
            bits.ReadUe(65535);
            if (sequence.PictureOrderType == 0)
            {
                bits.ReadBits(sequence.PictureOrderBits);
                if (picture.BottomFieldPictureOrderPresent)
                {
                    bits.ReadSe();
                }
            }
            if (sequence.PictureOrderType == 1 && !sequence.DeltaPictureOrderAlwaysZero)
            {
                bits.ReadSe();
                if (picture.BottomFieldPictureOrderPresent)
                {
                    bits.ReadSe();
                }
            }
            if (picture.RedundantPictureCountPresent && bits.ReadUe(127) != 0)
            {
                throw new NotSupportedException("Redundant H.264 slices are not supported.");
            }
            bits.ReadFlag();
            bits.ReadFlag();
            int qp = picture.InitialQp + bits.ReadSe(-51, 51);
            if (qp is < 0 or > 51)
            {
                throw new InvalidDataException("The H.264 slice quantizer is outside the 8-bit range.");
            }
            int disableDeblocking = picture.DeblockingControlPresent ? bits.ReadUe(2) : 0;
            int alphaOffset = 0;
            int betaOffset = 0;
            if (picture.DeblockingControlPresent && disableDeblocking != 1)
            {
                alphaOffset = bits.ReadSe(-6, 6) * 2;
                betaOffset = bits.ReadSe(-6, 6) * 2;
            }
            return new H264IntraSliceHeader(sequence, picture, qp, disableDeblocking != 1, alphaOffset, betaOffset);
        }
    }
}
