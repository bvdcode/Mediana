// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal partial class Av1PictureDecoder
    {
        private Av1Picture Reconstruct<T>(Av1FrameHeader header, ReadOnlySpan<byte> tileData, bool separateHeader, CancellationToken cancellationToken)
            where T : unmanaged, IBinaryInteger<T>
        {
            bool inter = header.FrameType is Av1FrameType.Inter or Av1FrameType.Switch;
            references.Invalidate(header.InvalidReferences);
            Av1SequenceHeader geometry = header.Geometry;
            int bitDepth = typeof(T) == typeof(byte) ? 8 : geometry.BitDepth;
            int sampleBytes = Unsafe.SizeOf<T>();
            int contextBytes = checked(geometry.ContextCount * Unsafe.SizeOf<Av1BlockContext<T>>());
            Span<ushort> probabilities = stackalloc ushort[Av1DefaultCdfs.Count];
            ReadOnlySpan<ushort> initialProbabilities = default;
            if (inter && header.Inter.PrimaryReference != Av1InterPrediction.NoPrimaryReference)
            {
                initialProbabilities = MemoryMarshal.Cast<byte, ushort>(references.Get(header.Inter.References[header.Inter.PrimaryReference]).Cdfs.Memory.Span);
            }
            int cdefIndexCount = header.Cdef.IndexCount(geometry);
            int restorationBytes = checked(header.Restoration.UnitCount(header.PixelGeometry) * Unsafe.SizeOf<Av1RestorationUnit>());
            using WritableMediaBuffer pixels = allocator.Allocate(geometry.SampleByteCount);
            Av1SequenceHeader pixelGeometry = header.PixelGeometry;
            using WritableMediaBuffer? upscaled = header.SuperResolution.Active ? allocator.Allocate(pixelGeometry.SampleByteCount) : null;
            Span<T> pixelSamples = MemoryMarshal.Cast<byte, T>(pixels.Span);
            Span<T> upscaledSamples = upscaled is null ? default : MemoryMarshal.Cast<byte, T>(upscaled.Span);
            MediaBuffer? previousSegmentMap = null;
            if (header.Segmentation.Enabled && header.Inter.PrimaryReference != Av1InterPrediction.NoPrimaryReference)
            {
                Av1StoredPicture previous = references.Get(header.Inter.References[header.Inter.PrimaryReference]);
                if (previous.Header.Geometry.MiColumns == geometry.MiColumns && previous.Header.Geometry.MiRows == geometry.MiRows)
                {
                    previousSegmentMap = previous.SegmentMap;
                }
            }
            ReadOnlySpan<byte> previousSegments = previousSegmentMap is null ? default : previousSegmentMap.Memory.Span;
            int segmentLength = header.Segmentation.Enabled && (header.Segmentation.UpdateMap || previousSegmentMap is null)
                ? geometry.MiColumns * geometry.MiRows : 0;
            using WritableMediaBuffer? segments = segmentLength != 0 ? allocator.Allocate(segmentLength) : null;
            int storedMotionLength = Av1MotionField.StoredLength(geometry, header);
            using WritableMediaBuffer? savedMotion = storedMotionLength != 0 ? allocator.Allocate(storedMotionLength) : null;
            using (WritableMediaBuffer? motions = inter || header.Intrabc ? allocator.Allocate(checked(geometry.MiColumns * geometry.MiRows * Unsafe.SizeOf<Av1MotionBlock>())) : null)
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
                Av1TilePayloadReader tiles = new(tileData, header.Tiles, !separateHeader);
                ReadOnlySpan<byte> selectedTile = default;
                while (true)
                {
                    if (!tiles.TryRead(out int tileIndex, out ReadOnlySpan<byte> encodedTile, cancellationToken))
                    {
                        if (selectedTile.IsEmpty) { break; }
                        tileIndex = header.Tiles.ContextUpdateTile;
                        encodedTile = selectedTile;
                        selectedTile = default;
                    }
                    else if (header.Tiles.Count > 1 && tileIndex == header.Tiles.ContextUpdateTile)
                    {
                        selectedTile = encodedTile;
                        continue;
                    }
                    Av1TileDecoder<T> tile = new(encodedTile, geometry, header, pixelSamples, probabilities, decoded.Span,
                        MemoryMarshal.Cast<byte, Av1BlockContext<T>>(contexts.Span), cancellationToken, initialProbabilities, references, motionBlocks, filterBlocks, cdefIndices, restorationUnits, motionField, segments is null ? default : segments.Span, previousSegments, tileIndex);
                    tile.Decode();
                }
                if (savedMotion is not null) { Av1MotionField.Store(geometry, header, references, motionBlocks, savedMotion.Span, cancellationToken); }
                if (header.LoopFilter.Enabled) { Av1LoopFilter.Apply(pixelSamples, geometry, header.LoopFilter, filterBlocks, cancellationToken, header.Segmentation, bitDepth); }
                int boundaryLength = Av1Restoration.BoundaryLength(pixelGeometry, header.Restoration);
                using WritableMediaBuffer? boundaryBuffer = boundaryLength != 0 ? allocator.Allocate(checked(boundaryLength * sampleBytes)) : null;
                Span<T> boundaries = boundaryBuffer is null ? default : MemoryMarshal.Cast<byte, T>(boundaryBuffer.Span);
                if (header.Restoration.Enabled)
                {
                    if (upscaled is null) { Av1Restoration.SaveBoundaries<T>(pixelSamples, geometry, header.Restoration, boundaries); }
                    else { Av1SuperResolutionUpscaling.SaveBoundariesCore<T>(pixelSamples, geometry, pixelGeometry, header.Restoration, boundaries, (1 << bitDepth) - 1, cancellationToken); }
                }
                if (header.Cdef.Enabled)
                {
                    using WritableMediaBuffer scratch = allocator.Allocate(checked(Av1Cdef.ScratchLength(geometry) * sampleBytes));
                    Av1Cdef.Apply(pixelSamples, geometry, header.Cdef, filterBlocks, cdefIndices, MemoryMarshal.Cast<byte, T>(scratch.Span), cancellationToken, bitDepth);
                }
                if (upscaled is not null) { Av1SuperResolutionUpscaling.FrameCore<T>(pixelSamples, geometry, upscaledSamples, pixelGeometry, (1 << bitDepth) - 1, cancellationToken); }
                if (header.Restoration.Enabled)
                {
                    using WritableMediaBuffer scratch = allocator.Allocate(checked(Av1Restoration.ScratchLength(pixelGeometry, header.Restoration) * sampleBytes));
                    Av1Restoration.Apply(upscaled is null ? pixelSamples : upscaledSamples, pixelGeometry, header.Restoration, restorationUnits, boundaries, MemoryMarshal.Cast<byte, T>(scratch.Span), cancellationToken, bitDepth);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            Av1Picture picture = new(header, upscaled is null ? pixels.Publish() : upscaled.Publish());
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
                    using MediaBuffer? segmentMap = segments?.Publish() ?? previousSegmentMap?.Retain();
                    references.Save(picture, probabilities, allocator, motionField, segmentMap);
                }
                transferred = true;
                return picture;
            }
            finally
            {
                if (!transferred) { picture.Dispose(); }
            }
        }
    }
}
