using System.Text;

namespace Mediana.Containers.Mp3
{
    internal ref struct Id3StringReader
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Utf16Little = new UnicodeEncoding(false, false, true);
        private static readonly Encoding Utf16Big = new UnicodeEncoding(true, false, true);
        private readonly ReadOnlySpan<byte> data;
        private readonly int code;
        private readonly int maxTextBytes;
        private Encoding? unicode;

        public Id3StringReader(ReadOnlySpan<byte> data, int code, int version, int maxTextBytes)
        {
            if (code is < 0 or > 3 || (version == 3 && code > 1))
            {
                throw new InvalidDataException("The ID3 text encoding is invalid for this version.");
            }
            this.data = data;
            this.code = code;
            this.maxTextBytes = maxTextBytes;
        }

        public int Position { get; private set; }

        public string ReadTerminated()
        {
            int end = FindTerminator();
            if (end == data.Length)
            {
                throw new InvalidDataException("The ID3 string lacks its required terminator.");
            }
            string value = Decode(data[Position..end]);
            Position = end + TerminatorBytes;
            return value;
        }

        public string[] ReadValues(bool multiple)
        {
            List<string> values = [];
            do
            {
                int end = FindTerminator();
                values.Add(Decode(data[Position..end]));
                Position = Math.Min(data.Length, end + TerminatorBytes);
                if (!multiple && Position < data.Length)
                {
                    Position = data.Length;
                }
            } while (Position < data.Length);
            return values.ToArray();
        }

        private int TerminatorBytes => code switch
        {
            0 or 3 => 1,
            1 or 2 => 2,
            _ => throw new InvalidOperationException()
        };

        private int FindTerminator()
        {
            int step = TerminatorBytes;
            int end = Position;
            while (end + step <= data.Length)
            {
                if (end - Position > maxTextBytes)
                {
                    throw new NotSupportedException("The ID3 string exceeds its configured byte limit.");
                }
                if (data[end] == 0 && (step == 1 || data[end + 1] == 0))
                {
                    return end;
                }
                end += step;
            }
            return data.Length;
        }

        private string Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > maxTextBytes)
            {
                throw new NotSupportedException("The ID3 string exceeds its configured byte limit.");
            }
            switch (code)
            {
                case 0:
                    return Encoding.Latin1.GetString(bytes);
                case 1:
                    Encoding? marked = null;
                    if (bytes.StartsWith([(byte)0xff, (byte)0xfe]))
                    {
                        marked = Utf16Little;
                    }
                    else if (bytes.StartsWith([(byte)0xfe, (byte)0xff]))
                    {
                        marked = Utf16Big;
                    }
                    if (marked is not null)
                    {
                        if (unicode is not null && unicode != marked)
                        {
                            throw new InvalidDataException("All UTF-16 strings in an ID3 frame must use the same byte order.");
                        }
                        unicode = marked;
                        bytes = bytes[2..];
                    }
                    if (bytes.IsEmpty)
                    {
                        return string.Empty;
                    }
                    return (unicode ?? throw new InvalidDataException("The ID3 UTF-16 string lacks its byte order mark.")).GetString(bytes);
                case 2:
                    return Utf16Big.GetString(bytes);
                case 3:
                    return Utf8.GetString(bytes);
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
