// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.H264
{
    internal class H264Configuration(int lengthSize,
        IReadOnlyDictionary<int, H264SequenceParameterSet> sequences,
        IReadOnlyDictionary<int, H264PictureParameterSet> pictures)
    {
        public int LengthSize { get; } = lengthSize;
        public IReadOnlyDictionary<int, H264SequenceParameterSet> Sequences { get; } = sequences;
        public IReadOnlyDictionary<int, H264PictureParameterSet> Pictures { get; } = pictures;
        private IReadOnlyDictionary<int, int> SequenceLengths { get; init; } = new Dictionary<int, int>();
        private IReadOnlyDictionary<int, int> PictureLengths { get; init; } = new Dictionary<int, int>();
        private int ConfigurationBytes { get; init; } = 7;

        public H264Configuration WithParameterSet(ReadOnlySpan<byte> nal, H264DecoderOptions options)
        {
            options.Validate();
            if (nal.Length > options.MaxParameterSetBytes)
            {
                throw new NotSupportedException("The AVC parameter set exceeds the configured byte limit.");
            }
            int type = H264Nal.Type(nal);
            if (type == 7)
            {
                H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, options);
                int bytes = UpdatedBytes(nal.Length, SequenceLengths, sequence.Id, options);
                Dictionary<int, H264SequenceParameterSet> updated = new(Sequences) { [sequence.Id] = sequence };
                Dictionary<int, int> lengths = new(SequenceLengths) { [sequence.Id] = nal.Length + 2 };
                return new H264Configuration(LengthSize, updated, Pictures)
                {
                    SequenceLengths = lengths,
                    PictureLengths = PictureLengths,
                    ConfigurationBytes = bytes
                };
            }
            if (type == 8)
            {
                H264PictureParameterSet picture = H264PictureParser.Parse(nal, Sequences);
                int bytes = UpdatedBytes(nal.Length, PictureLengths, picture.Id, options);
                Dictionary<int, H264PictureParameterSet> updated = new(Pictures) { [picture.Id] = picture };
                Dictionary<int, int> lengths = new(PictureLengths) { [picture.Id] = nal.Length + 2 };
                return new H264Configuration(LengthSize, Sequences, updated)
                {
                    SequenceLengths = SequenceLengths,
                    PictureLengths = lengths,
                    ConfigurationBytes = bytes
                };
            }
            throw new InvalidDataException("Expected an H.264 sequence or picture parameter set.");
        }

        private int UpdatedBytes(int length, IReadOnlyDictionary<int, int> lengths, int id, H264DecoderOptions options)
        {
            lengths.TryGetValue(id, out int previous);
            long total = (long)ConfigurationBytes - previous + length + 2;
            if (total > options.MaxConfigurationBytes)
            {
                throw new NotSupportedException("The AVC configuration exceeds the configured byte limit.");
            }
            return (int)total;
        }

        public static H264Configuration Parse(ReadOnlySpan<byte> data, H264DecoderOptions options)
        {
            options.Validate();
            if (data.Length > options.MaxConfigurationBytes)
            {
                throw new NotSupportedException("The AVC configuration exceeds the configured byte limit.");
            }
            if (data.Length < 7 || data[0] != 1 || (data[4] & 0xFC) != 0xFC || (data[5] & 0xE0) != 0xE0)
            {
                throw new InvalidDataException("The AVC configuration record is invalid.");
            }
            int lengthSize = (data[4] & 3) + 1;
            if (lengthSize == 3)
            {
                throw new InvalidDataException("The AVC NAL length size is reserved.");
            }
            int offset = 6;
            int sequenceCount = data[5] & 31;
            Dictionary<int, H264SequenceParameterSet> sequences = [];
            Dictionary<int, int> sequenceLengths = [];
            for (int index = 0; index < sequenceCount; index++)
            {
                ReadOnlySpan<byte> nal = ReadNal(data, ref offset, options);
                H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, options);
                if (!sequences.TryAdd(sequence.Id, sequence))
                {
                    throw new InvalidDataException("The AVC sequence parameter identifier is duplicated.");
                }
                sequenceLengths.Add(sequence.Id, nal.Length + 2);
            }
            int pictureCount = ReadByte(data, ref offset);
            Dictionary<int, H264PictureParameterSet> pictures = [];
            Dictionary<int, int> pictureLengths = [];
            for (int index = 0; index < pictureCount; index++)
            {
                ReadOnlySpan<byte> nal = ReadNal(data, ref offset, options);
                H264PictureParameterSet picture = H264PictureParser.Parse(nal, sequences);
                if (!pictures.TryAdd(picture.Id, picture))
                {
                    throw new InvalidDataException("The AVC picture parameter identifier is duplicated.");
                }
                pictureLengths.Add(picture.Id, nal.Length + 2);
            }
            if (offset < data.Length && data[1] is 100 or 110 or 122 or 144)
            {
                int chroma = ReadByte(data, ref offset);
                int lumaDepth = ReadByte(data, ref offset);
                int chromaDepth = ReadByte(data, ref offset);
                if ((chroma & 0xFC) != 0xFC || (lumaDepth & 0xF8) != 0xF8 || (chromaDepth & 0xF8) != 0xF8)
                {
                    throw new InvalidDataException("The AVC configuration extension reserved bits are invalid.");
                }
                int extensions = ReadByte(data, ref offset);
                if (extensions != 0)
                {
                    throw new NotSupportedException("AVC sequence parameter extensions are not supported.");
                }
            }
            if (offset != data.Length || sequences.Count == 0 || pictures.Count == 0)
            {
                throw new InvalidDataException("The AVC configuration is incomplete or contains unexpected bytes.");
            }
            return new H264Configuration(lengthSize, sequences, pictures)
            {
                SequenceLengths = sequenceLengths,
                PictureLengths = pictureLengths,
                ConfigurationBytes = data.Length
            };
        }

        private static ReadOnlySpan<byte> ReadNal(ReadOnlySpan<byte> data, ref int offset, H264DecoderOptions options)
        {
            if (data.Length - offset < 2)
            {
                throw new InvalidDataException("The AVC parameter set length is truncated.");
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;
            if (length == 0 || length > data.Length - offset)
            {
                throw new InvalidDataException("The AVC parameter set is truncated or empty.");
            }
            if (length > options.MaxParameterSetBytes)
            {
                throw new NotSupportedException("The AVC parameter set exceeds the configured byte limit.");
            }
            ReadOnlySpan<byte> nal = data.Slice(offset, length);
            offset += length;
            return nal;
        }

        private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
        {
            if (offset == data.Length)
            {
                throw new InvalidDataException("The AVC configuration is truncated.");
            }
            return data[offset++];
        }
    }
}
