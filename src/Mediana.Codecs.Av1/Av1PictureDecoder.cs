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
                bits.Finish();
                return references.Show(reference);
            }
            Av1FrameHeader header = Av1FrameHeader.Parse(frame, geometry, references);
            references.Invalidate(header.InvalidReferences);
            geometry = header.Geometry;
            int sampleCount = geometry.SampleCount;
            int contextBytes = checked(geometry.ContextCount * Unsafe.SizeOf<Av1BlockContext>());
            Span<ushort> probabilities = stackalloc ushort[Av1DefaultCdfs.Count];
            using WritableMediaBuffer pixels = allocator.Allocate(sampleCount);
            using (WritableMediaBuffer contexts = allocator.Allocate(contextBytes))
            using (WritableMediaBuffer decoded = allocator.Allocate(geometry.SampleCount / 16))
            {
                Av1TileDecoder tile = new(frame[header.TileOffset..], geometry, header, pixels.Span, probabilities, decoded.Span,
                    MemoryMarshal.Cast<byte, Av1BlockContext>(contexts.Span), cancellationToken);
                tile.Decode();
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
                        Av1TileProbabilities.Initialize(probabilities, header.Quantization.BaseIndex);
                    }
                    references.Save(picture, probabilities, allocator);
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
