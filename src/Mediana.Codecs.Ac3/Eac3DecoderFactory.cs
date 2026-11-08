// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Managed E-AC-3 decoder for independent and AC-3-converted substreams using conventional transforms and coupling.
    /// Each packet contains one or more syncframes, totalling at most six audio blocks. Output covers their combined PCM.
    /// Adaptive hybrid transforms, enhanced coupling, spectral extension and transient pre-noise processing are rejected.</summary>
    public class Eac3DecoderFactory(bool applyDynamicRangeCompression = true) : Ac3DecoderFactory(applyDynamicRangeCompression)
    {
        public override string CodecId => Ac3FrameHeader.EnhancedCodecId;
        public override string ImplementationId => "mediana.eac3";
        protected override bool Enhanced => true;
        protected override EncodedStreamFormat ExpectedFormat => StreamFormats.Eac3;
        protected override EncodedStreamFormat ExpectedMp4Format => StreamFormats.Eac3Mp4;
    }
}
