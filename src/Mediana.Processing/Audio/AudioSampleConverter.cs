// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    public static class AudioSampleConverter
    {
        /// <summary>Returns independently disposable PCM in the requested sample format and layout.
        /// Matching storage is retained without copying and remains charged to its original allocator.
        /// A representation change allocates output from the supplied allocator. Floating-point input
        /// must be finite; matching floating-point storage preserves every sample bit, including headroom.</summary>
        public static AudioFrame ConvertOrRetain(AudioFrame input, AudioSampleFormat sampleFormat, AudioStorageLayout layout,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Format.SampleFormat != sampleFormat || input.Format.Layout != layout)
            {
                return Apply(input, sampleFormat, layout, allocator, cancellationToken);
            }
            for (int plane = 0; plane < input.Format.PlaneCount; plane++)
            {
                ReadOnlySpan<byte> samples = input.GetPlane(plane).Span;
                switch (sampleFormat)
                {
                    case AudioSampleFormat.Unsigned8:
                    case AudioSampleFormat.Signed16:
                    case AudioSampleFormat.Signed32:
                        break;
                    case AudioSampleFormat.Float32:
                        ValidateFinite(MemoryMarshal.Cast<byte, float>(samples), cancellationToken);
                        break;
                    case AudioSampleFormat.Float64:
                        ValidateFinite(MemoryMarshal.Cast<byte, double>(samples), cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(sampleFormat));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return input.Retain();
        }

        /// <summary>Converts native-endian PCM storage without changing rate, channels or timing.
        /// Borrows input and always copies or converts into the supplied allocator, including matching formats.
        /// Integer output saturates and rounds to nearest even
        /// without dithering. Float output preserves finite headroom; non-finite samples are rejected.</summary>
        public static AudioFrame Apply(AudioFrame input, AudioSampleFormat sampleFormat, AudioStorageLayout layout,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            AudioFormat format = new(input.Format.SampleRate, sampleFormat, layout, input.Format.Channels.ToArray());
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(input.SamplesPerChannel));
            if (input.Format.SampleFormat == sampleFormat && input.Format.Layout == layout &&
                sampleFormat is AudioSampleFormat.Unsigned8 or AudioSampleFormat.Signed16 or AudioSampleFormat.Signed32)
            {
                CopyStorage(input, writable.Memory.Span, cancellationToken);
            }
            else if (input.Format.Layout == AudioStorageLayout.Interleaved && layout == AudioStorageLayout.Interleaved &&
                sampleFormat == AudioSampleFormat.Signed16 && input.Format.SampleFormat is AudioSampleFormat.Float32 or AudioSampleFormat.Signed32)
            {
                ReadOnlySpan<byte> source = input.GetPlane(0).Span;
                Span<short> destination = MemoryMarshal.Cast<byte, short>(writable.Memory.Span);
                switch (input.Format.SampleFormat)
                {
                    case AudioSampleFormat.Float32:
                        Pcm16Converter.Convert(MemoryMarshal.Cast<byte, float>(source), destination, cancellationToken);
                        break;
                    case AudioSampleFormat.Signed32:
                        Pcm16Converter.Convert(MemoryMarshal.Cast<byte, int>(source), destination, cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(input));
                }
            }
            else
            {
                for (int sample = 0; sample < input.SamplesPerChannel; sample++)
                {
                    if ((sample & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    for (int channel = 0; channel < format.Channels.Count; channel++)
                    {
                        int index = format.Layout switch
                        {
                            AudioStorageLayout.Interleaved => sample * format.Channels.Count + channel,
                            AudioStorageLayout.Planar => channel * input.SamplesPerChannel + sample,
                            _ => throw new ArgumentOutOfRangeException(nameof(layout))
                        };
                        PcmSampleAccess.Write(writable.Memory.Span[(index * format.BytesPerSample)..], sampleFormat,
                            PcmSampleAccess.Read(input, sample, channel));
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, input.SamplesPerChannel, buffer, input.Timestamp);
        }

        private static void CopyStorage(AudioFrame input, Span<byte> output, CancellationToken token)
        {
            const int BlockBytes = 16 * 1024;
            int destination = 0;
            for (int plane = 0; plane < input.Format.PlaneCount; plane++)
            {
                ReadOnlySpan<byte> source = input.GetPlane(plane).Span;
                for (int offset = 0; offset < source.Length;)
                {
                    token.ThrowIfCancellationRequested();
                    int count = Math.Min(BlockBytes, source.Length - offset);
                    source.Slice(offset, count).CopyTo(output.Slice(destination, count));
                    offset += count;
                    destination += count;
                }
            }
        }

        private static void ValidateFinite<T>(ReadOnlySpan<T> samples, CancellationToken token)
            where T : IFloatingPointIeee754<T>
        {
            for (int index = 0; index < samples.Length; index++)
            {
                if ((index & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (!T.IsFinite(samples[index]))
                {
                    throw new InvalidDataException("Audio processing requires finite PCM samples.");
                }
            }
        }
    }
}
