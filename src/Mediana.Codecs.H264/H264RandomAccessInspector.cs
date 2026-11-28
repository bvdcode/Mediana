// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;

namespace Mediana.Codecs.H264
{
    /// <summary>Inspects AVC packets with slices in increasing macroblock order without reconstructing pictures.
    /// IDR results validate access-unit boundaries and all supported slice headers,
    /// not residual data or complete macroblock coverage. Each call uses the original configuration plus parameters in that packet.</summary>
    public class H264RandomAccessInspector : IRandomAccessInspector
    {
        private readonly int streamIndex;
        private readonly bool hasCompleteAccessUnits;
        private readonly H264Configuration configuration;
        private readonly H264DecoderOptions options;

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default) =>
            IsIdr(packet, cancellationToken);

        public H264RandomAccessInspector(EncodedStreamInfo stream, H264DecoderOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != "h264")
            {
                throw new NotSupportedException("AVC random access inspection requires an H.264 video stream.");
            }
            StreamFormats.Avc.Require(stream.Format);
            this.options = options ?? new();
            this.options.Validate();
            configuration = H264Configuration.Parse(stream.Configuration.Span, this.options.HeaderLimits);
            streamIndex = stream.StreamIndex;
            hasCompleteAccessUnits = true;
        }

        internal H264RandomAccessInspector(EncodedStreamInfo stream, H264Configuration configuration, H264DecoderOptions options)
        {
            this.configuration = configuration;
            this.options = options;
            streamIndex = stream.StreamIndex;
            hasCompleteAccessUnits = stream.Format.Boundary == EncodedPacketBoundary.AccessUnit;
        }

        /// <summary>Ignores the container key-frame flag. Non-IDR slice headers, including B headers,
        /// are not fully parsed. IDR headers require a supported coded picture starting at macroblock zero.</summary>
        public bool IsIdr(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet does not belong to the configured AVC stream.", nameof(packet));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!hasCompleteAccessUnits) { return false; }
            H264AccessUnit unit = H264AccessUnitReader.Read(packet.Data, configuration, options, cancellationToken);
            if (unit.Slices.Count == 0 || H264Nal.Type(unit.Slices[0].Span) != 5)
            {
                return false;
            }
            H264SliceHeaders.Read(unit, options, cancellationToken);
            return true;
        }
    }
}
