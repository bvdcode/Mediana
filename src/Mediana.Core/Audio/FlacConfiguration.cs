using System.Buffers.Binary;

namespace Mediana.Core.Audio
{
    /// <summary>Immutable native FLAC STREAMINFO. A zero total sample count means unknown.
    /// The stored MD5 is descriptive; parsing does not verify decoded audio integrity.</summary>
    public class FlacConfiguration
    {
        public const string CodecId = "flac";
        public const int StreamInfoBytes = 34;

        private FlacConfiguration(ReadOnlySpan<byte> data)
        {
            MinimumBlockSize = BinaryPrimitives.ReadUInt16BigEndian(data);
            MaximumBlockSize = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
            MinimumFrameBytes = ReadUInt24(data[4..]);
            MaximumFrameBytes = ReadUInt24(data[7..]);
            ulong audio = BinaryPrimitives.ReadUInt64BigEndian(data[10..]);
            SampleRate = (int)(audio >> 44);
            ChannelCount = (int)((audio >> 41) & 7) + 1;
            BitsPerSample = (int)((audio >> 36) & 31) + 1;
            TotalSamples = (long)(audio & 0xfffffffff);
            Md5 = data[18..].ToArray();
            if (MinimumBlockSize < 16 || MaximumBlockSize < MinimumBlockSize || BitsPerSample < 4
                || (MinimumFrameBytes != 0 && MaximumFrameBytes != 0 && MinimumFrameBytes > MaximumFrameBytes))
            {
                throw new InvalidDataException("The FLAC STREAMINFO block sizes, frame sizes or sample precision are invalid.");
            }
            if (SampleRate == 0)
            {
                throw new NotSupportedException("FLAC non-audio streams without a sample rate are not supported.");
            }
        }

        public int MinimumBlockSize { get; }
        public int MaximumBlockSize { get; }
        public int MinimumFrameBytes { get; }
        public int MaximumFrameBytes { get; }
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int BitsPerSample { get; }
        public long TotalSamples { get; }
        public ReadOnlyMemory<byte> Md5 { get; }

        /// <summary>Speaker mask for the default FLAC channel order, before any Vorbis comment override.</summary>
        public uint DefaultChannelMask => ChannelCount switch
        {
            1 => 0x4,
            2 => 0x3,
            3 => 0x7,
            4 => 0x33,
            5 => 0x37,
            6 => 0x3f,
            7 => 0x70f,
            8 => 0x63f,
            _ => throw new InvalidOperationException("The FLAC channel count is invalid.")
        };

        public static FlacConfiguration ReadStreamInfo(ReadOnlySpan<byte> data)
        {
            if (data.Length != StreamInfoBytes)
            {
                throw new InvalidDataException("FLAC STREAMINFO must contain exactly 34 bytes.");
            }
            return new FlacConfiguration(data);
        }

        private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] << 16 | data[1] << 8 | data[2];
    }
}
