// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Formats
{
    /// <summary>Encoded representations produced and consumed by the built-in format modules.</summary>
    public static class StreamFormats
    {
        public static EncodedStreamFormat Avc { get; } = new("avc.decoder-configuration-record", "avc.length-prefixed", EncodedPacketBoundary.AccessUnit);
        /// <summary>Annex B byte ranges with optional opening SPS NAL without a start code. Packet boundaries may cut across access units.</summary>
        public static EncodedStreamFormat AvcAnnexB { get; } = new("avc.sequence-parameter-set", "avc.annex-b", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat Hevc { get; } = new("hevc.decoder-configuration-record", "hevc.length-prefixed", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Av1 { get; } = new("av1.codec-configuration-record", "av1.low-overhead.access-unit", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Vp9 { get; } = new("none", "vp9.access-unit", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Vp8 { get; } = new("none", "vp8.complete-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat ProRes { get; } = new("none", "prores.frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Dv { get; } = new("none", "dv.dif-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Ffv1 { get; } = new("ffv1.coded-size-and-extradata", "ffv1.frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Dts { get; } = new("none", "dts.core-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mjpeg { get; } = new("none", "jpeg.complete-picture", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Aac { get; } = new("aac.audio-specific-config", "aac.raw-data-block", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat AacAdts { get; } = new("aac.audio-specific-config", "aac.adts-frame", EncodedPacketBoundary.TransportFrame);
        public static EncodedStreamFormat Ac3 { get; } = new("none", "ac3.syncframe", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Eac3Syncframe { get; } = new("none", "eac3.syncframe", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Eac3 { get; } = new("none", "eac3.syncframes", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Ac3Mp4 { get; } = new("ac3.dac3", "ac3.syncframe", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Eac3Mp4 { get; } = new("eac3.dec3", "eac3.syncframes", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mp3 { get; } = new("mpeg-audio.frame-header", "mpeg-audio.layer-iii-frame", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Alac { get; } = new("alac.specific-config", "alac.access-unit", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Flac { get; } = new("flac.stream-info", "flac.frames", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat FlacFrame { get; } = new("flac.stream-info", "flac.frames", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Opus { get; } = new("opus.identification-header", "opus.packet", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Mpeg2Video { get; } = new("none", "mpeg2.video", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat Mpeg4Visual { get; } = new("mpeg4.visual.headers", "mpeg4.visual.vop", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat MicrosoftMpeg4V3 { get; } = new("msmpeg4v3.picture-description", "msmpeg4v3.picture", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat AacLatmByteStream { get; } = new("none", "aac.latm", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat AacAdtsByteStream { get; } = new("mpeg4.audio-specific-config", "aac.adts", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat Ac3ByteStream { get; } = new("none", "ac3.syncframes", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat Eac3ByteStream { get; } = new("none", "eac3.syncframes", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat DtsByteStream { get; } = new("none", "dts.syncframes", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat WaveFormatMediaObjects { get; } =
            new("waveformatex", "asf.media-object", EncodedPacketBoundary.ArbitraryBytes);
        public static EncodedStreamFormat Wmv2 { get; } = new("wmv2.picture-description", "wmv2.picture", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Theora { get; } = new("theora.xiph-laced-headers", "theora.packet", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat Vorbis { get; } = new("vorbis.xiph-laced-headers", "vorbis.packet", EncodedPacketBoundary.AccessUnit);
        public static EncodedStreamFormat WavePcm { get; } = new("wave.format", "pcm.samples", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat WaveALaw { get; } = new("wave.format", "g711.alaw.samples", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat WaveMuLaw { get; } = new("wave.format", "g711.mulaw.samples", EncodedPacketBoundary.SampleBlock);
        /// <summary>Blocks with WAVE channel headers. Packet duration, if set, counts valid sample frames and may trim the final block.</summary>
        public static EncodedStreamFormat WaveImaAdpcm { get; } = new("wave.format", "adpcm.ima.wave.blocks", EncodedPacketBoundary.SampleBlock);
        /// <summary>Blocks with WAVE predictor headers. Packet duration, if set, counts valid sample frames and may trim the final block.</summary>
        public static EncodedStreamFormat WaveMicrosoftAdpcm { get; } = new("wave.format", "adpcm.ms.wave.blocks", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat AiffPcm { get; } = new("aiff.common", "pcm.samples", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat AiffIma4 { get; } = new("aiff.common", "adpcm.ima.apple.blocks", EncodedPacketBoundary.SampleBlock);
        public static EncodedStreamFormat QuickTimeTimecode32 { get; } = new("quicktime.timecode-description", "quicktime.timecode32-be", EncodedPacketBoundary.SampleBlock);
    }
}
