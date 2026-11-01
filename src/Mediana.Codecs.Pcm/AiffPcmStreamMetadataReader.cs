// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Formats;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Reads PCM parameters from a FORM type and AIFF/AIFF-C common-chunk configuration.</summary>
    public class AiffPcmStreamMetadataReader : PcmStreamMetadataReader
    {
        public override string ImplementationId => "mediana.pcm.aiff.metadata";
        public override string CodecId => PcmConfiguration.AiffCodecId;
        protected override EncodedStreamFormat ExpectedFormat => StreamFormats.AiffPcm;

        protected override PcmConfiguration? ReadConfiguration(ReadOnlySpan<byte> bytes) =>
            PcmConfiguration.ReadAiffFormat(bytes, out _);
    }
}
