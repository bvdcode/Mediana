// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    /// <summary>Reads AAC core and signaled enhancement parameters from AudioSpecificConfig.
    /// Reading parameters does not require decoder support for the channel configuration.</summary>
    public class AacStreamMetadataReader : IStreamMetadataReader
    {
        private static readonly CodecProfileInfo LowComplexity = new("LC");
        private static readonly CodecProfileInfo HighEfficiency = new("HE-AAC");
        private static readonly CodecProfileInfo HighEfficiencyStereo = new("HE-AAC v2");

        public string ImplementationId => "mediana.aac.metadata";
        public string CodecId => AacConfiguration.CodecId;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not AAC audio.", nameof(stream));
            }
            StreamFormats.Aac.Require(stream.Format);
            AacConfiguration? configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out _);
            if (configuration is null)
            {
                return null;
            }
            CodecProfileInfo profile = configuration.ParametricStereo ? HighEfficiencyStereo :
                configuration.SpectralBandReplication == true ? HighEfficiency : LowComplexity;
            return new AudioStreamMetadata(configuration.OutputSampleRate, configuration.ParametricStereo ? 2 : configuration.ChannelCount)
            {
                CodecProfile = profile
            };
        }
    }
}
