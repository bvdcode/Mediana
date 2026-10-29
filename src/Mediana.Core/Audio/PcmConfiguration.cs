using System.Buffers.Binary;

namespace Mediana.Core.Audio
{
    /// <summary>Little-endian PCM described by WAVEFORMATEX or WAVEFORMATEXTENSIBLE.
    /// Decoded integer samples are left-aligned in their native-endian storage type.
    /// Unassigned extensible channels retain their interleaved order as discrete channels.</summary>
    public class PcmConfiguration
    {
        public const string CodecId = "pcm";
        private static readonly Guid pcmSubformat = new("00000001-0000-0010-8000-00aa00389b71");
        private static readonly Guid floatSubformat = new("00000003-0000-0010-8000-00aa00389b71");
        private static readonly string[] speakers = ["front-left", "front-right", "front-center", "low-frequency",
            "back-left", "back-right", "front-left-of-center", "front-right-of-center", "back-center", "side-left", "side-right",
            "top-center", "top-front-left", "top-front-center", "top-front-right", "top-back-left", "top-back-center", "top-back-right"];

        private PcmConfiguration(AudioFormat format, int sourceBits, int validBits)
        {
            Format = format;
            SourceBitsPerSample = sourceBits;
            ValidBitsPerSample = validBits;
        }

        public AudioFormat Format { get; }
        public int SourceBitsPerSample { get; }
        public int ValidBitsPerSample { get; }
        public int BlockAlignment => Format.Channels.Count * SourceBitsPerSample / 8;

        /// <summary>Returns null with a reason for an unsupported encoding; malformed descriptions throw.</summary>
        public static PcmConfiguration? ReadWaveFormat(ReadOnlySpan<byte> configuration, out string? unsupportedReason)
        {
            unsupportedReason = null;
            if (configuration.Length < 16)
            {
                throw new InvalidDataException("The PCM wave format is truncated.");
            }
            int tag = BinaryPrimitives.ReadUInt16LittleEndian(configuration);
            int channels = BinaryPrimitives.ReadUInt16LittleEndian(configuration[2..]);
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(configuration[4..]);
            uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(configuration[8..]);
            int alignment = BinaryPrimitives.ReadUInt16LittleEndian(configuration[12..]);
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(configuration[14..]);
            bool extensible = tag == 0xfffe;
            int validBits = bits;
            uint mask = 0;
            if (extensible)
            {
                if (configuration.Length < 40 || BinaryPrimitives.ReadUInt16LittleEndian(configuration[16..]) < 22
                    || BinaryPrimitives.ReadUInt16LittleEndian(configuration[16..]) > configuration.Length - 18)
                {
                    throw new InvalidDataException("The extensible PCM wave format is truncated.");
                }
                validBits = BinaryPrimitives.ReadUInt16LittleEndian(configuration[18..]);
                mask = BinaryPrimitives.ReadUInt32LittleEndian(configuration[20..]);
                Guid subformat = new(configuration.Slice(24, 16));
                if (subformat == pcmSubformat)
                {
                    tag = 1;
                }
                else if (subformat == floatSubformat)
                {
                    tag = 3;
                }
                else
                {
                    unsupportedReason = "The extensible wave subformat is not PCM or IEEE float.";
                    return null;
                }
            }
            if (tag is not (1 or 3))
            {
                unsupportedReason = "The wave format is not PCM or IEEE float.";
                return null;
            }
            if (!extensible && tag == 3 && (configuration.Length < 18
                || BinaryPrimitives.ReadUInt16LittleEndian(configuration[16..]) > configuration.Length - 18))
            {
                throw new InvalidDataException("The IEEE float wave format extension is truncated.");
            }
            if (channels is < 1 or > 32 || rate is 0 or > int.MaxValue || bits == 0 || bits % 8 != 0
                || validBits <= 0 || validBits > bits || alignment != channels * bits / 8 || byteRate != (long)rate * alignment)
            {
                throw new InvalidDataException("The PCM wave rate, precision or block alignment is invalid.");
            }
            if ((!extensible && channels > 2) || (mask >> speakers.Length) != 0)
            {
                unsupportedReason = "The PCM wave channel assignment is not supported.";
                return null;
            }
            AudioSampleFormat? sampleFormat = (tag, bits) switch
            {
                (1, 8) => AudioSampleFormat.Unsigned8,
                (1, 16) => AudioSampleFormat.Signed16,
                (1, 24 or 32) => AudioSampleFormat.Signed32,
                (3, 32) => AudioSampleFormat.Float32,
                (3, 64) => AudioSampleFormat.Float64,
                _ => null
            };
            if (sampleFormat is null)
            {
                unsupportedReason = "The PCM wave sample representation is not supported.";
                return null;
            }
            if (tag == 3 && validBits != bits)
            {
                throw new InvalidDataException("IEEE float precision must equal its sample container size.");
            }
            List<string> labels = [];
            if (extensible)
            {
                for (int bit = 0; bit < speakers.Length && labels.Count < channels; bit++)
                {
                    if ((mask & (1U << bit)) != 0)
                    {
                        labels.Add(speakers[bit]);
                    }
                }
                while (labels.Count < channels)
                {
                    labels.Add($"discrete-{labels.Count + 1}");
                }
            }
            else
            {
                labels.AddRange(channels switch
                {
                    1 => ["mono"],
                    2 => new[] { "front-left", "front-right" },
                    _ => throw new InvalidDataException("The PCM wave channel count is invalid.")
                });
            }
            return new PcmConfiguration(new AudioFormat((int)rate, sampleFormat.Value, AudioStorageLayout.Interleaved, labels.ToArray()), bits, validBits);
        }
    }
}
