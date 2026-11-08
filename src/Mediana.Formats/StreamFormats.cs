// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Formats
{
    /// <summary>Encoded representations produced and consumed by the built-in format modules.</summary>
    public static class StreamFormats
    {
        public static EncodedStreamFormat Avc { get; } = new("avc.decoder-configuration-record", "avc.length-prefixed", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Hevc { get; } = new("hevc.decoder-configuration-record", "hevc.length-prefixed", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Av1 { get; } = new("av1.codec-configuration-record", "av1.low-overhead.access-unit", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Vp9 { get; } = new("none", "vp9.access-unit", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Vp8 { get; } = new("none", "vp8.complete-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mjpeg { get; } = new("none", "jpeg.complete-picture", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Aac { get; } = new("aac.audio-specific-config", "aac.raw-data-block", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Ac3 { get; } = new("none", "ac3.syncframe", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Eac3 { get; } = new("none", "eac3.syncframes", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Ac3Mp4 { get; } = new("ac3.dac3", "ac3.syncframe", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Eac3Mp4 { get; } = new("eac3.dec3", "eac3.syncframes", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mp3 { get; } = new("mpeg-audio.frame-header", "mpeg-audio.layer-iii-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Flac { get; } = new("flac.stream-info", "flac.frames", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat Opus { get; } = new("opus.identification-header", "opus.packet", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mpeg4Visual { get; } = new("mpeg4.visual.headers", "mpeg4.visual.vop", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Theora { get; } = new("theora.xiph-laced-headers", "theora.packet", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Vorbis { get; } = new("vorbis.xiph-laced-headers", "vorbis.packet", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat WavePcm { get; } = new("wave.format", "pcm.samples", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat AiffPcm { get; } = new("aiff.common", "pcm.samples", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat QuickTimeTimecode32 { get; } = new("quicktime.timecode-description", "quicktime.timecode32-be", EncodedPacketBoundary.SampleBlock);
    }
}
