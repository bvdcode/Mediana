// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    /// <summary>Reads MPEG-4 Visual object-layer geometry and visual-object-sequence profile.</summary>
    public class Mpeg4StreamMetadataReader : IStreamMetadataReader
    {
        public string ImplementationId => "mediana.mpeg4.metadata";
        public string CodecId => Mpeg4VisualConfiguration.CodecId;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Mpeg4Visual || stream.Configuration.IsEmpty) { return null; }
            ReadOnlySpan<byte> bytes = stream.Configuration.Span;
            Mpeg4VisualConfiguration layer = Mpeg4VisualConfiguration.Read(bytes);
            VideoStreamConfiguration description = new(layer.Width, layer.Height, VideoChromaSampling.Sampling420, 8, 8,
                !layer.Interlaced, new(layer.AspectNumerator, layer.AspectDenominator))
            {
                PixelFormat = VideoPixelFormat.Yuv420P8,
                NominalFrameRate = layer.FixedTimeIncrement is int duration ? new(layer.TimeIncrementResolution, duration) : null,
                CodecProfile = ReadProfile(bytes)
            };
            return new VideoStreamMetadata([description]);
        }

        private static CodecProfileInfo? ReadProfile(ReadOnlySpan<byte> bytes)
        {
            int start = bytes.IndexOf(new byte[] { 0, 0, 1, 0xb0 });
            if (start < 0 || bytes.Length - start < 5) { return null; }
            int id = bytes[start + 4];
            return id switch
            {
                >= 0xf0 and <= 0xf5 => new("Advanced Simple", (id & 15).ToString(CultureInfo.InvariantCulture)),
                0xf7 => new("Advanced Simple", "3b"),
                >= 1 and <= 6 => new("Simple", id.ToString(CultureInfo.InvariantCulture)),
                8 => new("Simple", "0"),
                9 => new("Simple", "0b"),
                _ => null
            };
        }
    }
}
