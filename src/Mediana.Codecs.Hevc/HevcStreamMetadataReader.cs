// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Hevc
{
    /// <summary>Inspects opening HEVC sequence parameter sets from the decoder configuration.</summary>
    public class HevcStreamMetadataReader : IStreamMetadataReader
    {
        public string ImplementationId => "mediana.hevc.metadata";
        public string CodecId => HevcDecoderConfiguration.CodecId;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Hevc) { return null; }
            HevcDecoderConfiguration record = HevcDecoderConfiguration.Read(stream.Configuration);
            if (record.ProfileSpace != 0 || record.Profile is not (1 or 2 or 3)) { return null; }
            List<VideoStreamConfiguration> configurations = [];
            foreach (HevcNalArray array in record.Arrays)
            {
                if (array.Type != 33) { continue; }
                foreach (ReadOnlyMemory<byte> nal in array.Units)
                {
                    SequenceParameters sequence = SequenceParameters.Read(new(nal), int.MaxValue);
                    configurations.Add(Describe(sequence, record));
                }
            }
            return configurations.Count == 0 ? null : new VideoStreamMetadata(configurations);
        }

        private static VideoStreamConfiguration Describe(SequenceParameters sequence, HevcDecoderConfiguration record)
        {
            VideoChromaSampling chroma = sequence.ChromaFormat switch
            {
                0 => VideoChromaSampling.Monochrome,
                1 => VideoChromaSampling.Sampling420,
                2 => VideoChromaSampling.Sampling422,
                3 => VideoChromaSampling.Sampling444,
                _ => throw new InvalidDataException("Invalid HEVC chroma sampling.")
            };
            string profile = sequence.Profile switch
            {
                1 => "Main",
                2 => "Main 10",
                3 => "Main Still Picture",
                _ => string.Concat("Profile ", sequence.Profile.ToString(CultureInfo.InvariantCulture))
            };
            string? level = record.Level != 0 ? (record.Level / 30m).ToString("0.#", CultureInfo.InvariantCulture) : null;
            VideoPixelFormat? pixels = (chroma, sequence.BitDepth) switch
            {
                (VideoChromaSampling.Sampling420, 8) => VideoPixelFormat.Yuv420P8,
                (VideoChromaSampling.Sampling420, 10) => VideoPixelFormat.Yuv420P10,
                (VideoChromaSampling.Sampling422, 8) => VideoPixelFormat.Yuv422P8,
                (VideoChromaSampling.Sampling422, 10) => VideoPixelFormat.Yuv422P10,
                (VideoChromaSampling.Sampling444, 8) => VideoPixelFormat.Yuv444P8,
                (VideoChromaSampling.Sampling444, 10) => VideoPixelFormat.Yuv444P10,
                _ => null
            };
            return new(sequence.VisibleWidth, sequence.VisibleHeight, chroma, sequence.BitDepth,
                chroma == VideoChromaSampling.Monochrome ? null : sequence.BitDepth,
                sampleAspectRatio: sequence.Video.AspectRatio, color: HevcVideoFormat.ReadColor(sequence.Video))
            {
                CodecProfile = new(profile, level),
                PixelFormat = pixels
            };
        }
    }
}
