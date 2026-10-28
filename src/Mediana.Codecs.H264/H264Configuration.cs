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
            for (int index = 0; index < sequenceCount; index++)
            {
                H264SequenceParameterSet sequence = H264SequenceParser.Parse(ReadNal(data, ref offset, options), options);
                if (!sequences.TryAdd(sequence.Id, sequence))
                {
                    throw new InvalidDataException("The AVC sequence parameter identifier is duplicated.");
                }
            }
            int pictureCount = ReadByte(data, ref offset);
            Dictionary<int, H264PictureParameterSet> pictures = [];
            for (int index = 0; index < pictureCount; index++)
            {
                H264PictureParameterSet picture = H264PictureParser.Parse(ReadNal(data, ref offset, options), sequences);
                if (!pictures.TryAdd(picture.Id, picture))
                {
                    throw new InvalidDataException("The AVC picture parameter identifier is duplicated.");
                }
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
            return new H264Configuration(lengthSize, sequences, pictures);
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
