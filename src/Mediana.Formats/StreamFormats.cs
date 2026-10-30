// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Formats
{
    /// <summary>Encoded representations produced and consumed by the built-in format modules.</summary>
    public static class StreamFormats
    {
        public static EncodedStreamFormat Avc { get; } = new("avc.decoder-configuration-record", "avc.length-prefixed", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Aac { get; } = new("aac.audio-specific-config", "aac.raw-data-block", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mp3 { get; } = new("mpeg-audio.frame-header", "mpeg-audio.layer-iii-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Flac { get; } = new("flac.stream-info", "flac.frames", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat WavePcm { get; } = new("wave.format", "pcm.samples", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat AiffPcm { get; } = new("aiff.common", "pcm.samples", EncodedPacketBoundary.SampleBlock);
    }
}
