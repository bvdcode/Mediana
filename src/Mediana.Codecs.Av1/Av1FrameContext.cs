// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1FrameContext(Av1FrameType Type, bool ShowFrame, bool Showable, bool ErrorResilient,
        bool DisableCdfUpdate, bool ScreenContent, int FrameId, int RefreshReferences, int InvalidReferences, int OrderHint,
        Av1SequenceHeader Geometry, int RenderWidth, int RenderHeight, Av1InterPrediction Inter, Av1FrameTiming Timing = default, Av1SuperResolution SuperResolution = default)
    {
        internal bool Intra => Type is Av1FrameType.Key or Av1FrameType.IntraOnly;

        internal static Av1FrameContext Read(ref Av1BitReader bits, Av1SequenceHeader sequence, Av1ReferencePictures pictures)
        {
            Av1FrameType type = Av1FrameType.Key;
            bool showFrame = true;
            bool showable = false;
            bool errorResilient = true;
            uint? presentationTime = null;
            if (!sequence.Tools.ReducedStillPicture)
            {
                if (bits.Flag())
                {
                    throw new InvalidDataException("An AV1 frame OBU must not use show-existing syntax.");
                }
                type = (Av1FrameType)bits.Read(2);
                showFrame = bits.Flag();
                if (showFrame) { presentationTime = sequence.Timing?.ReadPresentationTime(ref bits); }
                showable = showFrame ? type != Av1FrameType.Key : bits.Flag();
                errorResilient = type == Av1FrameType.Switch || (type == Av1FrameType.Key && showFrame) || bits.Flag();
            }
            bool intra = type is Av1FrameType.Key or Av1FrameType.IntraOnly;
            bool disableCdfUpdate = bits.Flag();
            bool screenContent = sequence.Tools.ForceScreenContent switch
            {
                0 => false,
                1 => true,
                2 => bits.Flag(),
                _ => throw new InvalidDataException("The AV1 sequence screen-content control is invalid.")
            };
            bool integerMotion = screenContent && sequence.Tools.ForceIntegerMotion switch
            {
                0 => false,
                1 => true,
                2 => bits.Flag(),
                _ => throw new InvalidDataException("The AV1 sequence motion precision control is invalid.")
            };
            integerMotion |= intra;
            int frameId = bits.Read(sequence.Tools.FrameIdBits);
            int invalid = pictures.Expired(frameId, sequence.Tools.FrameIdBits, sequence.Tools.DeltaFrameIdBits);
            bool overrideSize = type == Av1FrameType.Switch || (!sequence.Tools.ReducedStillPicture && bits.Flag());
            int orderHint = bits.Read(sequence.Tools.OrderHintBits);
            int primary = intra || errorResilient ? Av1InterPrediction.NoPrimaryReference : bits.Read(3);
            uint? removalTime = sequence.Timing?.ReadBufferRemovalTime(ref bits);
            int refresh = type == Av1FrameType.Switch || (type == Av1FrameType.Key && showFrame)
                ? Av1ReferencePictures.All : bits.Read(8);
            if (type == Av1FrameType.IntraOnly && refresh == Av1ReferencePictures.All)
            {
                throw new InvalidDataException("An AV1 intra-only frame cannot refresh every reference slot.");
            }
            if (intra && refresh == Av1ReferencePictures.All) { invalid = Av1ReferencePictures.All; }
            if ((!intra || refresh != Av1ReferencePictures.All) && errorResilient && sequence.Tools.OrderHintBits != 0)
            {
                for (int index = 0; index < Av1ReferencePictures.Count; index++)
                {
                    if (bits.Read(sequence.Tools.OrderHintBits) != pictures.OrderHint(index)) { invalid |= 1 << index; }
                }
            }
            Av1ReferenceIndices references = default;
            if (!intra)
            {
                bool shortSignaling = sequence.Tools.OrderHintBits != 0 && bits.Flag();
                if (shortSignaling)
                {
                    int last = bits.Read(3);
                    int golden = bits.Read(3);
                    Span<int> hints = stackalloc int[Av1ReferencePictures.Count];
                    for (int index = 0; index < hints.Length; index++) { hints[index] = pictures.OrderHint(index); }
                    references = Av1ReferenceOrdering.Resolve(hints, orderHint, sequence.Tools.OrderHintBits, last, golden);
                }
                for (int index = 0; index < Av1ReferenceIndices.Count; index++)
                {
                    int slot = shortSignaling ? references[index] : bits.Read(3);
                    if ((invalid & (1 << slot)) != 0)
                    {
                        throw new InvalidDataException("The AV1 frame references an expired picture.");
                    }
                    Av1StoredPicture stored = pictures.Get(slot);
                    references[index] = slot;
                    if (sequence.Tools.FrameIdBits != 0)
                    {
                        int difference = bits.Read(sequence.Tools.DeltaFrameIdBits) + 1;
                        int expected = (frameId - difference) & ((1 << sequence.Tools.FrameIdBits) - 1);
                        if (expected != stored.Header.FrameId)
                        {
                            throw new InvalidDataException("The AV1 reference frame identifier does not match.");
                        }
                    }
                }
            }
            int renderWidth = 0;
            int renderHeight = 0;
            bool inherited = false;
            if (!intra && overrideSize && !errorResilient)
            {
                for (int index = 0; index < Av1ReferenceIndices.Count; index++)
                {
                    if (!bits.Flag()) { continue; }
                    Av1FrameHeader reference = pictures.Get(references[index]).Header;
                    sequence = sequence with { Width = reference.PixelGeometry.Width, Height = reference.Geometry.Height };
                    renderWidth = reference.RenderWidth;
                    renderHeight = reference.RenderHeight;
                    inherited = true;
                    break;
                }
            }
            if (!inherited && overrideSize)
            {
                int width = bits.Read(sequence.Tools.WidthBits) + 1;
                int height = bits.Read(sequence.Tools.HeightBits) + 1;
                if (width > sequence.Width || height > sequence.Height)
                {
                    throw new InvalidDataException("The AV1 frame dimensions exceed the declared sequence maximum.");
                }
                sequence = sequence with { Width = width, Height = height };
            }
            int upscaledWidth = sequence.Width;
            Av1SuperResolution superResolution = Av1SuperResolution.Read(ref bits, sequence);
            if (superResolution.Active) { sequence = sequence with { Width = superResolution.CodedWidth }; }
            if (!inherited)
            {
                renderWidth = upscaledWidth;
                renderHeight = sequence.Height;
                if (bits.Flag())
                {
                    renderWidth = bits.Read(16) + 1;
                    renderHeight = bits.Read(16) + 1;
                }
            }
            Av1InterPrediction inter = new(references, primary, integerMotion, false, 0, false, false);
            if (intra)
            {
                if (screenContent && sequence.Width == upscaledWidth && bits.Flag())
                {
                    throw new NotSupportedException("AV1 intra block copy reconstruction is not implemented.");
                }
            }
            else
            {
                bool highPrecision = !integerMotion && bits.Flag();
                int interpolation = bits.Flag() ? 4 : bits.Read(2);
                bool switchable = bits.Flag();
                bool referenceMotion = !errorResilient && sequence.Tools.ReferenceMotion && bits.Flag();
                inter = new(references, primary, integerMotion, highPrecision, interpolation, switchable, referenceMotion);
            }
            return new(type, showFrame, showable, errorResilient, disableCdfUpdate, screenContent, frameId,
                refresh, invalid, orderHint, sequence, renderWidth, renderHeight, inter, new(presentationTime, removalTime), superResolution);
        }
    }
}
