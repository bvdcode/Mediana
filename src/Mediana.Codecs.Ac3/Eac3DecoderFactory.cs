// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Managed E-AC-3 decoder for complete independent/dependent frame sets and AC-3-converted substreams, including adaptive transforms,
    /// both coupling modes, spectral extension and transient pre-noise processing. Up to 16 rendered channels and six audio blocks per packet.
    /// Output preserves combined packet timing and is delayed until transient corrections are complete. Drain at EOF.</summary>
    public class Eac3DecoderFactory(bool applyDynamicRangeCompression = true) : Ac3DecoderFactory(applyDynamicRangeCompression)
    {
        public override string CodecId => Ac3FrameHeader.EnhancedCodecId;
        public override string ImplementationId => "mediana.eac3";
        protected override bool Enhanced => true;
        protected override EncodedStreamFormat ExpectedFormat => StreamFormats.Eac3;
        protected override EncodedStreamFormat ExpectedMp4Format => StreamFormats.Eac3Mp4;
    }
}
