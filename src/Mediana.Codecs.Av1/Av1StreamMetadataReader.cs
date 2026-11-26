// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Av1
{
    /// <summary>Reads AV1 picture geometry and sample labels from an opening intra picture.</summary>
    public class Av1StreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.av1.metadata";
        public string CodecId => Av1CodecConfigurationRecord.CodecId;
        public int PacketProbeLength => 8192;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return null;
        }

        public StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Av1) { return null; }
            Av1CodecConfigurationRecord record = Av1CodecConfigurationRecord.Read(stream.Configuration.Span);
            if (record.Profile != 0 || record.BitDepth is not (8 or 10)) { return null; }
            Av1Configuration configuration = Av1Configuration.Read(stream.Configuration.Span);
            Av1SequenceHeader? sequence = configuration.Sequence;
            Formats.Video.Av1ObuReader reader = new(prefix, allowImplicitFinalSize: true);
            while (reader.TryReadPrefix(out int type, out ReadOnlySpan<byte> payload, out bool complete))
            {
                if (reader.SpatialId != 0 || reader.TemporalId != 0) { return null; }
                if (type == 1)
                {
                    if (!complete) { return null; }
                    sequence = Av1SequenceHeader.Parse(payload);
                    configuration.Validate(sequence.Value);
                }
                else if (type is 3 or 6 && sequence is Av1SequenceHeader opening && !payload.IsEmpty)
                {
                    return ReadPicture(stream, opening, payload);
                }
                if (!complete) { break; }
            }
            return null;
        }

        private static VideoStreamMetadata? ReadPicture(EncodedStreamInfo stream, Av1SequenceHeader sequence, ReadOnlySpan<byte> payload)
        {
            Av1BitReader bits = new(payload);
            if (!sequence.Tools.ReducedStillPicture &&
                (bits.Flag() || (Av1FrameType)bits.Read(2) is not (Av1FrameType.Key or Av1FrameType.IntraOnly)))
            {
                return null;
            }
            bits = new(payload);
            Av1FrameContext context = Av1FrameContext.Read(ref bits, sequence, new Av1ReferencePictures());
            Av1SequenceHeader geometry = context.SuperResolution.PixelGeometry(context.Geometry);
            SampleAspectRatio aspect = Av1VideoFormat.ReadAspectRatio(geometry, context.RenderWidth, context.RenderHeight);
            int level = geometry.Tools.Level;
            string? levelName = level <= 23 ? FormattableString.Invariant($"{2 + level / 4}.{level % 4}") : null;
            VideoStreamConfiguration description = new(geometry.Width, geometry.Height,
                geometry.Monochrome ? VideoChromaSampling.Monochrome : VideoChromaSampling.Sampling420,
                geometry.BitDepth, geometry.Monochrome ? null : geometry.BitDepth, true,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, Av1VideoFormat.ReadColor(geometry))
            {
                CodecProfile = new("Main", levelName),
                PixelFormat = Av1VideoFormat.ReadPixelFormat(geometry)
            };
            return new([description]);
        }
    }
}
