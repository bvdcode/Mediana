// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    /// <summary>Prepares a fixed PCM format and channel permutation in one output allocation.
    /// Preserves channel identities, sample count, rate and timing; does not mix or resample.</summary>
    public class AudioFramePreparation
    {
        private readonly AudioFormat inputFormat;
        private readonly int[] mapping;
        private readonly bool sameOrder;

        public AudioFramePreparation(AudioFormat inputFormat, AudioFormat outputFormat)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(outputFormat);
            if (inputFormat.SampleRate != outputFormat.SampleRate || inputFormat.Channels.Count != outputFormat.Channels.Count)
            {
                throw new ArgumentException("PCM preparation must retain the sample rate and channel set.", nameof(outputFormat));
            }
            this.inputFormat = inputFormat;
            OutputFormat = outputFormat;
            mapping = new int[outputFormat.Channels.Count];
            sameOrder = true;
            for (int destination = 0; destination < mapping.Length; destination++)
            {
                int source = -1;
                for (int channel = 0; channel < inputFormat.Channels.Count; channel++)
                {
                    if (inputFormat.Channels[channel] == outputFormat.Channels[destination])
                    {
                        source = channel;
                        break;
                    }
                }
                if (source < 0)
                {
                    throw new ArgumentException("PCM preparation cannot add or rename channels.", nameof(outputFormat));
                }
                mapping[destination] = source;
                sameOrder &= source == destination;
            }
        }

        public AudioFormat OutputFormat { get; }

        /// <summary>Borrows the input and returns an independently owned frame.
        /// Matching storage is retained without copying. Reordered output uses the supplied allocator.</summary>
        public AudioFrame PrepareOrRetain(AudioFrame input, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            PcmSampleAccess.RequireFormat(input.Format, inputFormat);
            if (sameOrder)
            {
                return AudioSampleConverter.ConvertOrRetain(input, OutputFormat.SampleFormat, OutputFormat.Layout, allocator, cancellationToken);
            }
            using WritableMediaBuffer writable = allocator.Allocate(OutputFormat.GetBufferSize(input.SamplesPerChannel));
            for (int sample = 0; sample < input.SamplesPerChannel; sample++)
            {
                if ((sample & 255) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                for (int channel = 0; channel < mapping.Length; channel++)
                {
                    int index = OutputFormat.Layout switch
                    {
                        AudioStorageLayout.Interleaved => sample * mapping.Length + channel,
                        AudioStorageLayout.Planar => channel * input.SamplesPerChannel + sample,
                        _ => throw new ArgumentOutOfRangeException(nameof(OutputFormat))
                    };
                    double value = PcmSampleAccess.Read(input, sample, mapping[channel]);
                    PcmSampleAccess.Write(writable.Span[(index * OutputFormat.BytesPerSample)..], OutputFormat.SampleFormat, value);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new(OutputFormat, input.SamplesPerChannel, buffer, input.Timestamp);
        }
    }
}
