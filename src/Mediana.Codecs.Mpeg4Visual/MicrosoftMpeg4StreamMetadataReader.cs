// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    /// <summary>Reads coded geometry of Microsoft MPEG-4 v3 or Windows Media Video 8.</summary>
    public class MicrosoftMpeg4StreamMetadataReader(string codecId) : IStreamMetadataReader
    {
        private readonly EncodedStreamFormat format = codecId switch
        {
            MicrosoftMpeg4Configuration.Version3CodecId => StreamFormats.MicrosoftMpeg4V3,
            MicrosoftMpeg4Configuration.Wmv2CodecId => StreamFormats.Wmv2,
            _ => throw new ArgumentOutOfRangeException(nameof(codecId))
        };

        public string ImplementationId => $"mediana.{CodecId}.metadata";
        public string CodecId { get; } = codecId;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != format) { return null; }
            MicrosoftMpeg4Configuration configuration = MicrosoftMpeg4Configuration.Read(stream.Configuration);
            return new VideoStreamMetadata([new(configuration.Width, configuration.Height,
                VideoChromaSampling.Sampling420, 8, 8, true, stream.AspectRatioDescription?.Resolve(null))
            {
                PixelFormat = VideoPixelFormat.Yuv420P8
            }]);
        }
    }
}
