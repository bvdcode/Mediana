// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Formats;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Decodes PCM configured by a FORM type and AIFF/AIFF-C common-chunk payload.</summary>
    public class AiffPcmDecoderFactory(int maxSamplesPerFrame = 65536) : PcmDecoderFactory(maxSamplesPerFrame)
    {
        public override string ImplementationId => "mediana.pcm.aiff";
        public override string CodecId => PcmConfiguration.AiffCodecId;
        protected override EncodedStreamFormat ExpectedFormat => StreamFormats.AiffPcm;

        protected override PcmConfiguration? ReadConfiguration(ReadOnlySpan<byte> bytes, out string? unsupportedReason) =>
            PcmConfiguration.ReadAiffFormat(bytes, out unsupportedReason);
    }
}
