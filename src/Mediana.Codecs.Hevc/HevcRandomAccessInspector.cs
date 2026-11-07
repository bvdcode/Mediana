// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;

namespace Mediana.Codecs.Hevc
{
    /// <summary>Checks IRAP slice headers and picture boundaries independently of container key-frame flags.</summary>
    public class HevcRandomAccessInspector : IRandomAccessInspector
    {
        private readonly int streamIndex;
        private readonly HevcConfiguration configuration;
        private readonly HevcDecoderOptions options;

        public HevcRandomAccessInspector(EncodedStreamInfo stream, HevcDecoderOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != "hevc") { throw new NotSupportedException("HEVC random access inspection requires HEVC video."); }
            StreamFormats.Hevc.Require(stream.Format);
            this.options = options ?? new(); this.options.Validate();
            configuration = HevcConfiguration.Read(stream.Configuration, this.options); streamIndex = stream.StreamIndex;
        }

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (packet.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to another HEVC stream.", nameof(packet)); }
            HevcAccessUnit unit = HevcAccessUnitReader.Read(packet.Data, configuration, options, cancellationToken);
            if (unit.Slices.Count == 0 || unit.Slices[0].NalType is < 16 or > 21) { return false; }
            HevcSliceHeaders.Partition(unit.Slices); return true;
        }
    }
}
