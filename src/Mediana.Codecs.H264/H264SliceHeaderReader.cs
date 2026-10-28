namespace Mediana.Codecs.H264
{
    internal static class H264SliceHeaderReader
    {
        public static H264SliceHeader Read(ref H264BitReader bits, H264Configuration configuration, byte nalHeader,
            H264DecoderOptions? options = null)
        {
            H264DecoderOptions limits = options ?? new H264DecoderOptions();
            limits.Validate();
            int nalType = nalHeader & 31;
            int referenceIndicator = (nalHeader >> 5) & 3;
            bool idr = nalType == 5;
            if ((nalHeader & 128) != 0 || nalType is not (1 or 5) || (idr && referenceIndicator == 0))
            {
                throw new InvalidDataException("Invalid H.264 slice NAL header.");
            }
            int firstMacroblock = bits.ReadUe();
            H264SliceKind kind = (bits.ReadUe(9) % 5) switch
            {
                0 => H264SliceKind.P,
                2 => H264SliceKind.I,
                1 or 3 or 4 => throw new NotSupportedException("B, SP and SI slices are not supported yet."),
                _ => throw new InvalidDataException("Invalid H.264 slice type.")
            };
            if (idr && kind != H264SliceKind.I)
            {
                throw new InvalidDataException("An H.264 IDR slice must be an I slice.");
            }
            int pictureId = bits.ReadUe(255);
            if (!configuration.Pictures.TryGetValue(pictureId, out H264PictureParameterSet? picture))
            {
                throw new InvalidDataException("The H.264 slice references an unknown picture parameter set.");
            }
            H264SequenceParameterSet sequence = configuration.Sequences[picture.SequenceId];
            ValidateConfiguration(sequence, picture, firstMacroblock);
            int frameNumber = (int)bits.ReadBits(sequence.FrameNumberBits);
            if (idr && frameNumber != 0)
            {
                throw new InvalidDataException("An H.264 IDR picture must have frame_num equal to zero.");
            }
            int? idrPictureId = idr ? bits.ReadUe(65535) : null;
            (int orderLsb, int deltaBottom, int delta0, int delta1) = ReadPictureOrder(ref bits, sequence, picture);
            if (picture.RedundantPictureCountPresent && bits.ReadUe(127) != 0)
            {
                throw new NotSupportedException("Redundant H.264 slices are not supported.");
            }
            int activeReferences = 0;
            IReadOnlyList<H264ReferenceModification> modifications = [];
            H264WeightTable? weights = null;
            if (kind == H264SliceKind.P)
            {
                activeReferences = bits.ReadFlag() ? bits.ReadUe(31) + 1 : picture.DefaultReferencesL0;
                modifications = H264ReferenceSyntax.ReadModifications(ref bits, activeReferences, 1 << sequence.FrameNumberBits);
                if (picture.WeightedPrediction)
                {
                    weights = H264WeightTable.Read(ref bits, activeReferences);
                }
            }
            bool noPriorOutput = false;
            bool longTermReference = false;
            bool adaptiveMarking = false;
            IReadOnlyList<H264MemoryOperation> operations = [];
            if (referenceIndicator != 0)
            {
                if (idr)
                {
                    noPriorOutput = bits.ReadFlag();
                    longTermReference = bits.ReadFlag();
                }
                else
                {
                    adaptiveMarking = bits.ReadFlag();
                    if (adaptiveMarking)
                    {
                        operations = H264ReferenceSyntax.ReadMarking(ref bits, 1 << sequence.FrameNumberBits, limits.MaxReferenceOperations);
                    }
                }
            }
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
            return new H264SliceHeader(sequence, picture)
            {
                Kind = kind,
                IsIdr = idr,
                ReferenceIndicator = referenceIndicator,
                FirstMacroblock = firstMacroblock,
                FrameNumber = frameNumber,
                IdrPictureId = idrPictureId,
                PictureOrderLsb = orderLsb,
                DeltaPictureOrderBottom = deltaBottom,
                DeltaPictureOrder0 = delta0,
                DeltaPictureOrder1 = delta1,
                ActiveReferencesL0 = activeReferences,
                ReferenceModifications = modifications,
                Weights = weights,
                NoOutputOfPriorPictures = noPriorOutput,
                LongTermReference = longTermReference,
                AdaptiveMarking = adaptiveMarking,
                MemoryOperations = operations,
                Qp = qp,
                DeblockingIdc = disableDeblocking,
                AlphaOffset = alphaOffset,
                BetaOffset = betaOffset
            };
        }

        private static (int Lsb, int DeltaBottom, int Delta0, int Delta1) ReadPictureOrder(ref H264BitReader bits,
            H264SequenceParameterSet sequence, H264PictureParameterSet picture)
        {
            int lsb = 0;
            int deltaBottom = 0;
            int delta0 = 0;
            int delta1 = 0;
            if (sequence.PictureOrderType == 0)
            {
                lsb = (int)bits.ReadBits(sequence.PictureOrderBits);
                if (picture.BottomFieldPictureOrderPresent)
                {
                    deltaBottom = bits.ReadSe();
                }
            }
            if (sequence.PictureOrderType == 1 && !sequence.DeltaPictureOrderAlwaysZero)
            {
                delta0 = bits.ReadSe();
                if (picture.BottomFieldPictureOrderPresent)
                {
                    delta1 = bits.ReadSe();
                }
            }
            return (lsb, deltaBottom, delta0, delta1);
        }

        private static void ValidateConfiguration(H264SequenceParameterSet sequence, H264PictureParameterSet picture, int firstMacroblock)
        {
            if (firstMacroblock >= sequence.WidthInMbs * sequence.HeightInMapUnits)
            {
                throw new InvalidDataException("The H.264 first macroblock is outside the picture.");
            }
            if (!sequence.FrameMbsOnly || picture.Cabac || sequence.ChromaFormat != 1 || sequence.SeparateColourPlane ||
                sequence.LumaBitDepth != 8 || sequence.ChromaBitDepth != 8 || sequence.ScalingMatrix ||
                sequence.TransformBypass || picture.Transform8x8)
            {
                throw new NotSupportedException("Reconstruction currently requires progressive 8-bit 4:2:0 CAVLC slices with default scaling.");
            }
        }
    }
}
