using System.Buffers.Binary;

namespace Mediana.Containers.Flac
{
    internal static class FlacMetadataParser
    {
        public static string ReadComments(ReadOnlySpan<byte> data, List<FlacComment> comments, FlacReadOptions options,
            CancellationToken cancellationToken)
        {
            FlacMetadataCursor cursor = new(data, options.MaxTextBytes);
            string vendor = cursor.ReadText(littleEndian: true);
            uint count = cursor.ReadUInt32(littleEndian: true);
            if (count > options.MaxComments)
            {
                throw new NotSupportedException("The FLAC comment count exceeds its configured limit.");
            }
            if (count > cursor.Remaining / sizeof(uint))
            {
                throw new InvalidDataException("The FLAC comment count exceeds its metadata block.");
            }
            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string field = cursor.ReadText(littleEndian: true);
                int separator = field.IndexOf('=');
                if (separator <= 0 || field.AsSpan(0, separator).ContainsAnyExceptInRange(' ', '~'))
                {
                    throw new InvalidDataException("A FLAC comment requires a printable ASCII name followed by '='.");
                }
                string name = field[..separator];
                if (name.Equals("WAVEFORMATEXTENSIBLE_CHANNEL_MASK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("Custom FLAC channel masks are not supported.");
                }
                comments.Add(new FlacComment(name, field[(separator + 1)..]));
            }
            cursor.RequireEnd();
            return vendor;
        }

        public static FlacPictureInfo ReadPicture(ReadOnlySpan<byte> data, FlacReadOptions options, out int offset)
        {
            FlacMetadataCursor cursor = new(data, options.MaxTextBytes);
            uint type = cursor.ReadUInt32();
            if (type > 20)
            {
                throw new NotSupportedException("The FLAC picture type is reserved.");
            }
            string mediaType = cursor.ReadText(ascii: true);
            string description = cursor.ReadText();
            uint width = cursor.ReadUInt32();
            uint height = cursor.ReadUInt32();
            uint bits = cursor.ReadUInt32();
            uint colors = cursor.ReadUInt32();
            uint length = cursor.ReadUInt32();
            if (length > cursor.Remaining)
            {
                throw new InvalidDataException("The FLAC picture extends past its metadata block.");
            }
            offset = cursor.Position;
            cursor.Read((int)length);
            cursor.RequireEnd();
            return new FlacPictureInfo(type, mediaType, description, width, height, bits, colors, (int)length);
        }

        public static void ValidateSeekTable(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
        {
            if (data.Length % 18 != 0)
            {
                throw new InvalidDataException("The FLAC seek table does not contain complete entries.");
            }
            ulong? previous = null;
            bool placeholder = false;
            for (int offset = 0; offset < data.Length; offset += 18)
            {
                if ((offset % (18 * 256)) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                ulong sample = BinaryPrimitives.ReadUInt64BigEndian(data[offset..]);
                if (sample == ulong.MaxValue)
                {
                    placeholder = true;
                }
                else
                {
                    if (placeholder || (previous is ulong number && sample <= number)
                        || BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 16)..]) == 0)
                    {
                        throw new InvalidDataException("FLAC seek points must be ordered unique sample numbers followed by placeholders.");
                    }
                    previous = sample;
                }
            }
        }
    }
}
