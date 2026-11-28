// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal static class H264SliceHeaderReader
    {
        public static H264SliceHeader Read(ref H264BitReader bits, H264Configuration configuration, byte nalHeader,
            int maxReferenceOperations = H264HeaderLimits.DefaultMaxReferenceOperations)
        {
            int nalType = nalHeader & 31;
            int referenceIndicator = (nalHeader >> 5) & 3;
            bool idr = nalType == 5;
            if ((nalHeader & 128) != 0 || nalType is not (1 or 5) || (idr && referenceIndicator == 0))
            {
                throw new InvalidDataException("Invalid H.264 slice NAL header.");
            }
            int firstMacroblock = bits.ReadUe();
            int sliceType = bits.ReadUe(9);
            H264SliceKind kind = (sliceType % 5) switch
            {
                0 => H264SliceKind.P,
                1 => H264SliceKind.B,
                2 => H264SliceKind.I,
                3 or 4 => throw new NotSupportedException("SP and SI slices are not supported yet."),
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
            ValidateConfiguration(sequence);
            int frameNumber = (int)bits.ReadBits(sequence.FrameNumberBits);
            H264PictureStructure structure = H264PictureStructure.Frame;
            if (!sequence.FrameMbsOnly && bits.ReadFlag())
            {
                structure = bits.ReadFlag() ? H264PictureStructure.BottomField : H264PictureStructure.TopField;
            }
            H264PictureLayout layout = new(sequence, structure);
            firstMacroblock = layout.FirstMacroblockAddress(firstMacroblock);
            if (idr && frameNumber != 0)
            {
                throw new InvalidDataException("An H.264 IDR picture must have frame_num equal to zero.");
            }
            int? idrPictureId = idr ? bits.ReadUe(65535) : null;
            (int orderLsb, int deltaBottom, int delta0, int delta1) = ReadPictureOrder(ref bits, sequence, picture, layout.IsField);
            if (picture.RedundantPictureCountPresent && bits.ReadUe(127) != 0)
            {
                throw new NotSupportedException("Redundant H.264 slices are not supported.");
            }
            bool directSpatialMotionPrediction = kind == H264SliceKind.B && bits.ReadFlag();
            int activeReferencesL0 = 0;
            int activeReferencesL1 = 0;
            IReadOnlyList<H264ReferenceModification> modifications = [];
            IReadOnlyList<H264ReferenceModification> modificationsL1 = [];
            H264WeightTable? weights = null;
            H264WeightTable? weightsL1 = null;
            if (kind is H264SliceKind.P or H264SliceKind.B)
            {
                bool overrideReferences = bits.ReadFlag();
                activeReferencesL0 = H264ReferenceSyntax.ReadActiveCount(ref bits, picture.DefaultReferencesL0,
                    overrideReferences, layout.IsField);
                if (kind == H264SliceKind.B)
                {
                    activeReferencesL1 = H264ReferenceSyntax.ReadActiveCount(ref bits, picture.DefaultReferencesL1,
                        overrideReferences, layout.IsField);
                }
                modifications = H264ReferenceSyntax.ReadModifications(ref bits, activeReferencesL0, layout.MaximumPictureNumber, layout.IsField);
                if (kind == H264SliceKind.B)
                {
                    modificationsL1 = H264ReferenceSyntax.ReadModifications(ref bits, activeReferencesL1, layout.MaximumPictureNumber, layout.IsField);
                }
                if ((kind == H264SliceKind.P && picture.WeightedPrediction)
                    || (kind == H264SliceKind.B && picture.WeightedBiPrediction == 1))
                {
                    (weights, weightsL1) = H264WeightTable.Read(ref bits, activeReferencesL0, activeReferencesL1);
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
                        operations = H264ReferenceSyntax.ReadMarking(ref bits, layout.MaximumPictureNumber, maxReferenceOperations, layout.IsField);
                    }
                }
            }
            int cabacInitIdc = 0;
            if (picture.Cabac && kind is H264SliceKind.P or H264SliceKind.B)
            {
                cabacInitIdc = bits.ReadUe(2);
            }
            H264Quantizer quantizer = new(sequence.LumaBitDepth);
            int qp = quantizer.FromSyntax(picture.InitialQp + bits.ReadSe(-quantizer.Maximum, quantizer.Maximum));
            int disableDeblocking = picture.DeblockingControlPresent ? bits.ReadUe(2) : 0;
            int alphaOffset = 0;
            int betaOffset = 0;
            if (picture.DeblockingControlPresent && disableDeblocking != 1)
            {
                alphaOffset = bits.ReadSe(-6, 6) * 2;
                betaOffset = bits.ReadSe(-6, 6) * 2;
            }
            return new H264SliceHeader(sequence, picture, structure)
            {
                Kind = kind,
                AllSlicesSameKind = sliceType >= 5,
                IsIdr = idr,
                ReferenceIndicator = referenceIndicator,
                FirstMacroblock = firstMacroblock,
                FrameNumber = frameNumber,
                IdrPictureId = idrPictureId,
                PictureOrderLsb = orderLsb,
                DeltaPictureOrderBottom = deltaBottom,
                DeltaPictureOrder0 = delta0,
                DeltaPictureOrder1 = delta1,
                DirectSpatialMotionPrediction = directSpatialMotionPrediction,
                ActiveReferencesL0 = activeReferencesL0,
                ActiveReferencesL1 = activeReferencesL1,
                ReferenceModifications = modifications,
                ReferenceModificationsL1 = modificationsL1,
                Weights = weights,
                WeightsL1 = weightsL1,
                NoOutputOfPriorPictures = noPriorOutput,
                LongTermReference = longTermReference,
                AdaptiveMarking = adaptiveMarking,
                MemoryOperations = operations,
                CabacInitIdc = cabacInitIdc,
                Qp = qp,
                DeblockingIdc = disableDeblocking,
                AlphaOffset = alphaOffset,
                BetaOffset = betaOffset
            };
        }

        private static (int Lsb, int DeltaBottom, int Delta0, int Delta1) ReadPictureOrder(ref H264BitReader bits,
            H264SequenceParameterSet sequence, H264PictureParameterSet picture, bool fieldPicture)
        {
            int lsb = 0;
            int deltaBottom = 0;
            int delta0 = 0;
            int delta1 = 0;
            if (sequence.PictureOrderType == 0)
            {
                lsb = (int)bits.ReadBits(sequence.PictureOrderBits);
                if (picture.BottomFieldPictureOrderPresent && !fieldPicture)
                {
                    deltaBottom = bits.ReadSe();
                }
            }
            if (sequence.PictureOrderType == 1 && !sequence.DeltaPictureOrderAlwaysZero)
            {
                delta0 = bits.ReadSe();
                if (picture.BottomFieldPictureOrderPresent && !fieldPicture)
                {
                    delta1 = bits.ReadSe();
                }
            }
            return (lsb, deltaBottom, delta0, delta1);
        }

        private static void ValidateConfiguration(H264SequenceParameterSet sequence)
        {
            string? reason = GetUnsupportedReason(sequence);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
        }

        public static string? GetUnsupportedReason(H264SequenceParameterSet sequence)
        {
            if (sequence.ChromaFormat is not (1 or 2 or 3) || sequence.SeparateColourPlane ||
                sequence.LumaBitDepth is not (8 or 10) || sequence.ChromaBitDepth != sequence.LumaBitDepth)
            {
                return "Slice header parsing currently requires 8-bit or 10-bit 4:2:0, 4:2:2 or 4:4:4 pictures.";
            }
            return null;
        }
    }
}
