// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.H264
{
    /// <summary>Reads all opening AVC SPS descriptions without I/O, picture allocation or decoder creation.
    /// Checks avcC structure and PPS identities/references, without parsing complete PPS reconstruction syntax.</summary>
    public class H264StreamMetadataReader : IStreamMetadataReader
    {
        private readonly H264MetadataOptions options;

        public H264StreamMetadataReader(H264MetadataOptions? options = null)
        {
            this.options = options ?? new H264MetadataOptions();
            this.options.Validate();
        }

        public string ImplementationId => "mediana.h264.metadata";
        public string CodecId => "h264";

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not H.264 video.", nameof(stream));
            }
            H264ConfigurationRecord record = new(stream.Configuration.Span, options.MaxConfigurationBytes, options.MaxParameterSetBytes);
            Dictionary<int, H264SequenceParameterSet> sequences = [];
            for (int index = 0; index < record.SequenceCount; index++)
            {
                ReadOnlySpan<byte> nal = record.ReadNal();
                H264BitReader prefix = H264Nal.Payload(nal, 7);
                if (!H264SequenceParser.IsSupportedProfile((int)prefix.ReadBits(8)))
                {
                    return null;
                }
                H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, maxPicturePixels: null);
                if (!sequences.TryAdd(sequence.Id, sequence))
                {
                    throw new InvalidDataException("The AVC sequence parameter identifier is duplicated.");
                }
            }
            int pictureCount = record.ReadPictureCount();
            HashSet<int> pictures = [];
            for (int index = 0; index < pictureCount; index++)
            {
                H264BitReader bits = H264Nal.Payload(record.ReadNal(), 8);
                int id = H264PictureParser.ReadIdentity(ref bits, sequences).Id;
                if (!pictures.Add(id))
                {
                    throw new InvalidDataException("The AVC picture parameter identifier is duplicated.");
                }
            }
            record.Finish();
            if (sequences.Count == 0 || pictures.Count == 0)
            {
                throw new InvalidDataException("The AVC configuration is incomplete.");
            }
            return new VideoStreamMetadata(sequences.Values.OrderBy(sequence => sequence.Id)
                .Select(sequence => new H264SequenceMetadata(sequence)));
        }
    }
}
