// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly partial record struct Av1FrameHeader(bool DisableCdfUpdate, bool ScreenContent, int TileOffset, Av1SequenceHeader Geometry, int RenderWidth, int RenderHeight, Av1Quantization Quantization, bool ReducedTransformSet, bool SelectTransform, bool ShowFrame, bool Showable, Av1FrameType FrameType,
        int RefreshReferences, int InvalidReferences, int OrderHint, bool DisableFrameEndUpdate, int FrameId = 0, Av1InterPrediction Inter = default, Av1LoopFilterParameters LoopFilter = default, Av1CdefParameters Cdef = default, Av1RestorationParameters Restoration = default, Av1BlockDeltaParameters Deltas = default, Av1FrameTiming Timing = default, Av1Segmentation Segmentation = default, Av1TileInfo Tiles = default, Av1GlobalMotion GlobalMotion = default, Av1GrainParameters? Grain = null)
    {
        internal static Av1FrameHeader Parse(ReadOnlySpan<byte> encoded, Av1SequenceHeader sequence, Av1ReferencePictures references, bool separateHeader = false)
        {
            Av1BitReader bits = new(encoded);
            Av1FrameContext context = Av1FrameContext.Read(ref bits, sequence, references);
            sequence = context.Geometry;
            bool disableCdfUpdate = context.DisableCdfUpdate;
            bool disableFrameEndUpdate = sequence.Tools.ReducedStillPicture || disableCdfUpdate || bits.Flag();
            Av1TileInfo tiles = Av1TileInfo.Read(ref bits, sequence);
            int quantizer = bits.Read(8);
            int yDc = ReadDelta(ref bits);
            int uDc = 0;
            int uAc = 0;
            int vDc = 0;
            int vAc = 0;
            if (!sequence.Monochrome)
            {
                bool different = sequence.SeparateUvDeltaQ && bits.Flag();
                uDc = ReadDelta(ref bits);
                uAc = ReadDelta(ref bits);
                vDc = different ? ReadDelta(ref bits) : uDc;
                vAc = different ? ReadDelta(ref bits) : uAc;
            }
            Av1Quantization quantization = new(quantizer, yDc, uDc, uAc, vDc, vAc);
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 quantization matrix syntax is not implemented.");
            }
            bool noPrimaryReference = context.Inter.PrimaryReference == Av1InterPrediction.NoPrimaryReference;
            Av1StoredPicture? primary = noPrimaryReference ? null : references.Get(context.Inter.References[context.Inter.PrimaryReference]);
            Av1Segmentation segmentation = Av1Segmentation.Read(ref bits, noPrimaryReference, primary?.Header.Segmentation ?? default);
            Av1BlockDeltaParameters deltas = Av1BlockDeltaParameters.Read(ref bits, quantizer);
            Av1LoopFilterParameters loopFilter = Av1LoopFilterParameters.Default;
            Av1CdefParameters cdef = default;
            Av1RestorationParameters restoration = default;
            bool selectTransform = false;
            if (!segmentation.CodedLossless(quantization))
            {
                Av1LoopFilterParameters previous = primary?.Header.LoopFilter ?? Av1LoopFilterParameters.Default;
                loopFilter = Av1LoopFilterParameters.Read(ref bits, sequence.Monochrome, previous);
                if (sequence.Cdef) { cdef = Av1CdefParameters.Read(ref bits, sequence.Monochrome); }
                if (sequence.Restoration) { restoration = Av1RestorationParameters.Read(ref bits, sequence); }
                selectTransform = bits.Flag();
            }
            Av1InterPrediction inter = context.Inter;
            if (!context.Intra)
            {
                bool selectReference = bits.Flag();
                inter = inter with { SelectReference = selectReference };
                bool skipMode = selectReference && sequence.Tools.OrderHintBits != 0 &&
                    Av1ReferenceOrdering.HasSkipPair(inter.References, references, context.OrderHint, sequence.Tools.OrderHintBits) && bits.Flag();
                inter = inter with { SkipMode = skipMode };
                bool warped = !context.ErrorResilient && sequence.Tools.WarpedMotion && bits.Flag();
                inter = inter with { WarpedMotion = warped };
            }
            bool reducedTransformSet = bits.Flag();
            Av1GlobalMotion globalMotion = Av1GlobalMotion.Read(ref bits, context.Intra, inter.HighPrecisionMotion, primary?.Header.GlobalMotion ?? default);
            Av1GrainParameters? grain = Av1GrainHeader.Read(ref bits, context, references);
            if (separateHeader) { bits.Finish(); }
            else { bits.Align(); }
            if (!separateHeader && bits.ByteOffset == encoded.Length)
            {
                throw new InvalidDataException("The AV1 frame has no tile payload.");
            }
            return new(disableCdfUpdate, context.ScreenContent, bits.ByteOffset, sequence, context.RenderWidth, context.RenderHeight, quantization, reducedTransformSet, selectTransform, context.ShowFrame, context.Showable, context.Type, context.RefreshReferences, context.InvalidReferences, context.OrderHint, disableFrameEndUpdate, context.FrameId, inter, loopFilter, cdef, restoration, deltas, context.Timing, segmentation, tiles, globalMotion, grain);
        }

        private static int ReadDelta(ref Av1BitReader bits) => bits.Flag() ? bits.ReadSigned(7) : 0;
    }
}
