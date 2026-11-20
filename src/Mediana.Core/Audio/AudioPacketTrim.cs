// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;

namespace Mediana.Core.Audio
{
    /// <summary>Stream-edge PCM samples excluded from presentation after decoding, per channel.
    /// Start trimming occurs only on the first packet, end trimming only on the last; counts may span packets.
    /// Coded packet timestamps and durations retain their complete decode extent.</summary>
    public readonly record struct AudioPacketTrim(int StartSamples, int EndSamples)
    {
        public const string Kind = "audio.presentation-trim";

        public MediaPacketSideData CreateSideData(MediaBufferAllocator allocator)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(StartSamples);
            ArgumentOutOfRangeException.ThrowIfNegative(EndSamples);
            using WritableMediaBuffer writable = allocator.Allocate(8);
            BinaryPrimitives.WriteInt32LittleEndian(writable.Memory.Span, StartSamples);
            BinaryPrimitives.WriteInt32LittleEndian(writable.Memory.Span[4..], EndSamples);
            using MediaBuffer buffer = writable.Publish();
            return new(Kind, buffer);
        }

        public static AudioPacketTrim Read(MediaPacketSideDataView data)
        {
            if (data.Kind != Kind || data.Data.Length != 8) { throw new InvalidDataException("The audio presentation trim side data is invalid."); }
            int start = BinaryPrimitives.ReadInt32LittleEndian(data.Data.Span);
            int end = BinaryPrimitives.ReadInt32LittleEndian(data.Data.Span[4..]);
            if (start < 0 || end < 0) { throw new InvalidDataException("Audio trim counts must be nonnegative."); }
            return new(start, end);
        }
    }
}
