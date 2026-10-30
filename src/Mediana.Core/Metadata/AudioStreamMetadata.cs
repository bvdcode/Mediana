// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Audio parameters established by codec configuration, without decoding samples.</summary>
    public class AudioStreamMetadata : StreamMetadata
    {
        public AudioStreamMetadata(int sampleRate, int channelCount, int? bitsPerSample = null,
            IEnumerable<string>? channelOrder = null) : base(MediaKind.Audio)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
            if (bitsPerSample is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
            }
            if (channelOrder is not null)
            {
                string[] snapshot = channelOrder.ToArray();
                if (snapshot.Length != channelCount || snapshot.Any(string.IsNullOrWhiteSpace)
                    || snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
                {
                    throw new ArgumentException("The channel order must name each channel exactly once.", nameof(channelOrder));
                }
                ChannelOrder = Array.AsReadOnly(snapshot);
            }
            SampleRate = sampleRate;
            ChannelCount = channelCount;
            BitsPerSample = bitsPerSample;
        }

        public int SampleRate { get; }
        public int ChannelCount { get; }
        /// <summary>Declared sample precision for PCM or lossless audio. Null for unspecified or lossy precision.</summary>
        public int? BitsPerSample { get; }
        /// <summary>Explicit codec-configuration channel order. Null means it has not been established.</summary>
        public IReadOnlyList<string>? ChannelOrder { get; }
    }
}
