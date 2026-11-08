// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal class Av1PictureDecoder(MediaBufferAllocator allocator, Av1Configuration? configuration = null) : IDisposable
    {
        private readonly Av1ReferencePictures references = new();
        private Av1SequenceHeader? savedSequence = configuration?.Sequence;

        internal Av1Picture Decode(ReadOnlySpan<byte> encoded, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            Av1ObuReader obus = new(encoded);
            Av1SequenceHeader? sequence = savedSequence;
            bool hasSequence = false;
            ReadOnlySpan<byte> frame = default;
            bool separateHeader = false;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (type)
                {
                    case 1:
                        if (hasSequence || !frame.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 access unit contains multiple sequence headers.");
                        }
                        sequence = Av1SequenceHeader.Parse(payload);
                        configuration?.Validate(sequence.Value);
                        hasSequence = true;
                        break;
                    case 2:
                        break;
                    case 3:
                    case 6:
                        if (sequence is null || !frame.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 access unit requires a sequence configuration and one frame.");
                        }
                        if (payload.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 frame OBU is empty.");
                        }
                        frame = payload;
                        separateHeader = type == 3;
                        break;
                    case 4:
                    case 7:
                    case 8:
                        throw new NotSupportedException("AV1 separate frame headers, tile groups and tile lists are not implemented.");
                    case 0:
                    case 5:
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                        break;
                    default:
                        throw new InvalidDataException("The AV1 OBU type is invalid.");
                }
            }
            if (sequence is not Av1SequenceHeader geometry || frame.IsEmpty)
            {
                throw new InvalidDataException("The AV1 access unit is missing its sequence or frame.");
            }
            if (separateHeader)
            {
                if (geometry.Tools.ReducedStillPicture || (hasSequence && sequence != savedSequence))
                {
                    throw new InvalidDataException("AV1 reference display requires an unchanged non-reduced sequence.");
                }
                Av1BitReader bits = new(frame);
                if (!bits.Flag())
                {
                    throw new NotSupportedException("AV1 separate frame headers and tile groups are not implemented.");
                }
                int reference = bits.Read(3);
                uint? presentationTime = geometry.Timing?.ReadPresentationTime(ref bits);
                if (geometry.Tools.FrameIdBits != 0 && bits.Read(geometry.Tools.FrameIdBits) != references.Get(reference).Header.FrameId)
                {
                    throw new InvalidDataException("The AV1 reference display identifier does not match.");
                }
                bits.Finish();
                return references.Show(reference, new(presentationTime, null));
            }
            Av1FrameHeader header = Av1FrameHeader.Parse(frame, geometry, references);
            bool inter = header.FrameType is Av1FrameType.Inter or Av1FrameType.Switch;
            references.Invalidate(header.InvalidReferences);
            geometry = header.Geometry;
            int sampleCount = geometry.SampleCount;
            int contextBytes = checked(geometry.ContextCount * Unsafe.SizeOf<Av1BlockContext>());
            Span<ushort> probabilities = stackalloc ushort[Av1DefaultCdfs.Count];
            ReadOnlySpan<ushort> initialProbabilities = default;
            if (inter && header.Inter.PrimaryReference != Av1InterPrediction.NoPrimaryReference)
            {
                initialProbabilities = MemoryMarshal.Cast<byte, ushort>(references.Get(header.Inter.References[header.Inter.PrimaryReference]).Cdfs.Memory.Span);
            }
            int cdefIndexCount = header.Cdef.IndexCount(geometry);
            int restorationBytes = checked(header.Restoration.UnitCount(geometry) * Unsafe.SizeOf<Av1RestorationUnit>());
            using WritableMediaBuffer pixels = allocator.Allocate(sampleCount);
            int storedMotionLength = Av1MotionField.StoredLength(geometry, header);
            using WritableMediaBuffer? savedMotion = storedMotionLength != 0 ? allocator.Allocate(storedMotionLength) : null;
            using (WritableMediaBuffer? motions = inter ? allocator.Allocate(checked(geometry.MiColumns * geometry.MiRows * Unsafe.SizeOf<Av1MotionBlock>())) : null)
            using (WritableMediaBuffer? projected = inter && header.Inter.ReferenceMotion ? allocator.Allocate(Av1MotionField.ByteLength(geometry)) : null)
            using (WritableMediaBuffer? filterMetadata = header.LoopFilter.Enabled || header.Cdef.Enabled
                ? allocator.Allocate(checked(geometry.MiColumns * geometry.MiRows * Unsafe.SizeOf<Av1FilterBlock>())) : null)
            using (WritableMediaBuffer? cdefMetadata = cdefIndexCount != 0 ? allocator.Allocate(cdefIndexCount) : null)
            using (WritableMediaBuffer? restorationMetadata = restorationBytes != 0 ? allocator.Allocate(restorationBytes) : null)
            using (WritableMediaBuffer contexts = allocator.Allocate(contextBytes))
            using (WritableMediaBuffer decoded = allocator.Allocate(geometry.SampleCount / 16))
            {
                Span<Av1MotionBlock> motionBlocks = motions is null ? default : MemoryMarshal.Cast<byte, Av1MotionBlock>(motions.Span);
                Span<Av1FilterBlock> filterBlocks = filterMetadata is null ? default : MemoryMarshal.Cast<byte, Av1FilterBlock>(filterMetadata.Span);
                Span<sbyte> cdefIndices = cdefMetadata is null ? default : MemoryMarshal.Cast<byte, sbyte>(cdefMetadata.Span);
                Span<Av1RestorationUnit> restorationUnits = restorationMetadata is null ? default : MemoryMarshal.Cast<byte, Av1RestorationUnit>(restorationMetadata.Span);
                Span<Av1MotionFieldSample> motionField = projected is null ? default : MemoryMarshal.Cast<byte, Av1MotionFieldSample>(projected.Span);
                if (projected is not null) { Av1MotionField.Estimate(geometry, header, references, motionField, cancellationToken); }
                Av1TileDecoder tile = new(frame[header.TileOffset..], geometry, header, pixels.Span, probabilities, decoded.Span,
                    MemoryMarshal.Cast<byte, Av1BlockContext>(contexts.Span), cancellationToken, initialProbabilities, references, motionBlocks, filterBlocks, cdefIndices, restorationUnits, motionField);
                tile.Decode();
                if (savedMotion is not null) { Av1MotionField.Store(geometry, header, references, motionBlocks, savedMotion.Span, cancellationToken); }
                if (header.LoopFilter.Enabled) { Av1LoopFilter.Apply(pixels.Span, geometry, header.LoopFilter, filterBlocks, cancellationToken); }
                int boundaryLength = Av1Restoration.BoundaryLength(geometry, header.Restoration);
                using WritableMediaBuffer? boundaryBuffer = boundaryLength != 0 ? allocator.Allocate(boundaryLength) : null;
                Span<byte> boundaries = boundaryBuffer is null ? default : boundaryBuffer.Span;
                if (header.Restoration.Enabled) { Av1Restoration.SaveBoundaries(pixels.Span, geometry, header.Restoration, boundaries); }
                if (header.Cdef.Enabled)
                {
                    using WritableMediaBuffer scratch = allocator.Allocate(Av1Cdef.ScratchLength(geometry));
                    Av1Cdef.Apply(pixels.Span, geometry, header.Cdef, filterBlocks, cdefIndices, scratch.Span, cancellationToken);
                }
                if (header.Restoration.Enabled)
                {
                    using WritableMediaBuffer scratch = allocator.Allocate(Av1Restoration.ScratchLength(geometry, header.Restoration));
                    Av1Restoration.Apply(pixels.Span, geometry, header.Restoration, restorationUnits, boundaries, scratch.Span, cancellationToken);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            savedSequence = sequence;
            Av1Picture picture = new(header, pixels.Publish());
            bool transferred = false;
            try
            {
                if (!geometry.Tools.ReducedStillPicture)
                {
                    if (header.DisableFrameEndUpdate)
                    {
                        Av1TileProbabilities.Initialize(probabilities, header.Quantization.BaseIndex, initialProbabilities);
                    }
                    using MediaBuffer? motionField = savedMotion?.Publish();
                    references.Save(picture, probabilities, allocator, motionField);
                }
                transferred = true;
                return picture;
            }
            finally
            {
                if (!transferred) { picture.Dispose(); }
            }
        }
        public void Dispose() => references.Dispose();
    }
}
