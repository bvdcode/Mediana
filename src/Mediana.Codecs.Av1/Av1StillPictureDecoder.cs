// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal static class Av1StillPictureDecoder
    {
        internal static Av1Picture Decode(ReadOnlySpan<byte> encoded, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            Av1ObuReader obus = new(encoded);
            Av1SequenceHeader? sequence = null;
            ReadOnlySpan<byte> frame = default;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (type)
                {
                    case 1:
                        if (sequence is not null || !frame.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 still picture contains multiple sequence headers.");
                        }
                        sequence = Av1SequenceHeader.Parse(payload);
                        break;
                    case 2:
                        if (!payload.IsEmpty)
                        {
                            throw new InvalidDataException("An AV1 temporal delimiter must be empty.");
                        }
                        break;
                    case 6:
                        if (sequence is null || !frame.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 still picture requires one sequence followed by one frame.");
                        }
                        if (payload.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 frame OBU is empty.");
                        }
                        frame = payload;
                        break;
                    case 3:
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
                throw new InvalidDataException("The AV1 still picture is missing its sequence or frame.");
            }
            Av1FrameHeader header = Av1FrameHeader.Parse(frame, geometry);
            int sampleCount = checked(geometry.Stride * geometry.PaddedHeight);
            int contextBytes = checked((geometry.MiColumns + Av1TileDecoder.LeftContexts) * Unsafe.SizeOf<Av1BlockContext>());
            using WritableMediaBuffer pixels = allocator.Allocate(sampleCount);
            using (WritableMediaBuffer contexts = allocator.Allocate(contextBytes))
            using (WritableMediaBuffer decoded = allocator.Allocate(checked(geometry.MiColumns * geometry.MiRows)))
            {
                Span<ushort> probabilities = stackalloc ushort[Av1DefaultCdfs.Count];
                Av1TileDecoder tile = new(frame[header.TileOffset..], geometry, header, pixels.Span, probabilities, decoded.Span,
                    MemoryMarshal.Cast<byte, Av1BlockContext>(contexts.Span), cancellationToken);
                tile.Decode();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(geometry, pixels.Publish());
        }
    }
}
