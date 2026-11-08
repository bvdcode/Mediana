// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly partial record struct Av1FrameHeader(bool DisableCdfUpdate, bool ScreenContent, int TileOffset, Av1SequenceHeader Geometry, int RenderWidth, int RenderHeight, Av1Quantization Quantization, bool ReducedTransformSet, bool SelectTransform, bool ShowFrame, bool Showable, Av1FrameType FrameType,
        int RefreshReferences, int InvalidReferences, int OrderHint, bool DisableFrameEndUpdate, int FrameId = 0, Av1InterPrediction Inter = default, Av1LoopFilterParameters LoopFilter = default, Av1CdefParameters Cdef = default, Av1RestorationParameters Restoration = default)
    {
        private const int MaximumTileWidth = 4096;
        private const int MaximumTileArea = 4096 * 2304;

        internal static Av1FrameHeader Parse(ReadOnlySpan<byte> encoded, Av1SequenceHeader sequence, Av1ReferencePictures references)
        {
            Av1BitReader bits = new(encoded);
            Av1FrameContext context = Av1FrameContext.Read(ref bits, sequence, references);
            sequence = context.Geometry;
            bool disableCdfUpdate = context.DisableCdfUpdate;
            bool disableFrameEndUpdate = sequence.Tools.ReducedStillPicture || disableCdfUpdate || bits.Flag();
            ReadSingleTile(ref bits, sequence);
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
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 segmentation is not implemented.");
            }
            if (quantizer > 0 && bits.Flag())
            {
                throw new NotSupportedException("AV1 block quantizer deltas are not implemented.");
            }
            Av1LoopFilterParameters loopFilter = Av1LoopFilterParameters.Default;
            Av1CdefParameters cdef = default;
            Av1RestorationParameters restoration = default;
            bool selectTransform = false;
            if (!quantization.Lossless)
            {
                Av1LoopFilterParameters previous = context.Inter.PrimaryReference == Av1InterPrediction.NoPrimaryReference
                    ? Av1LoopFilterParameters.Default : references.Get(context.Inter.References[context.Inter.PrimaryReference]).Header.LoopFilter;
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
            if (!context.Intra)
            {
                for (int index = 0; index < Av1ReferenceIndices.Count; index++)
                {
                    if (bits.Flag())
                    {
                        throw new NotSupportedException("AV1 non-identity global motion prediction is not implemented.");
                    }
                }
            }
            if (sequence.FilmGrain && (context.ShowFrame || context.Showable) && bits.Flag())
            {
                throw new NotSupportedException("AV1 film grain synthesis is not implemented.");
            }
            bits.Align();
            if (bits.ByteOffset == encoded.Length)
            {
                throw new InvalidDataException("The AV1 frame has no tile payload.");
            }
            return new(disableCdfUpdate, context.ScreenContent, bits.ByteOffset, sequence, context.RenderWidth, context.RenderHeight, quantization, reducedTransformSet, selectTransform, context.ShowFrame, context.Showable, context.Type, context.RefreshReferences, context.InvalidReferences, context.OrderHint, disableFrameEndUpdate, context.FrameId, inter, loopFilter, cdef, restoration);
        }

        private static int ReadDelta(ref Av1BitReader bits) => bits.Flag() ? bits.ReadSigned(7) : 0;

        private static void ReadSingleTile(ref Av1BitReader bits, Av1SequenceHeader sequence)
        {
            int shift = sequence.Superblock128 ? 5 : 4;
            int columns = (sequence.MiColumns + (1 << shift) - 1) >> shift;
            int rows = (sequence.MiRows + (1 << shift) - 1) >> shift;
            int side = 1 << (shift + 2);
            int widthLimit = MaximumTileWidth / side;
            int areaLimit = MaximumTileArea / (side * side);
            if (columns > widthLimit || checked(columns * rows) > areaLimit)
            {
                throw new NotSupportedException("AV1 multi-tile reconstruction is not implemented.");
            }
            if (bits.Flag())
            {
                if (columns > 1 && bits.Flag())
                {
                    throw new NotSupportedException("AV1 multi-column tiles are not implemented.");
                }
                if (rows > 1 && bits.Flag())
                {
                    throw new NotSupportedException("AV1 multi-row tiles are not implemented.");
                }
            }
            else
            {
                int width = bits.ReadNs(Math.Min(columns, widthLimit)) + 1;
                if (width != columns)
                {
                    throw new NotSupportedException("AV1 multi-column tiles are not implemented.");
                }
                int height = bits.ReadNs(Math.Min(rows, areaLimit / width)) + 1;
                if (height != rows)
                {
                    throw new NotSupportedException("AV1 multi-row tiles are not implemented.");
                }
            }
        }
    }
}
