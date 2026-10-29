namespace Mediana.Containers.Mp3
{
    internal static class Id3Binary
    {
        public static int ReadSynchsafe(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4 || (data[0] | data[1] | data[2] | data[3]) >= 128)
            {
                throw new InvalidDataException("The ID3 synchsafe integer has invalid or missing bytes.");
            }
            return data[0] << 21 | data[1] << 14 | data[2] << 7 | data[3];
        }

        public static int RemoveUnsynchronisation(Span<byte> data, CancellationToken cancellationToken)
        {
            int written = 0;
            for (int read = 0; read < data.Length; read++)
            {
                if ((read & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                byte value = data[read];
                data[written++] = value;
                if (value == 0xff && read + 1 < data.Length && data[read + 1] == 0)
                {
                    read++;
                }
            }
            return written;
        }
    }
}
