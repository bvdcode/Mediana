namespace Mediana.Core.Audio
{
    /// <summary>Native-endian PCM storage with explicit channel order. Channel labels are unique within the layout.</summary>
    public class AudioFormat
    {
        public AudioFormat(int sampleRate, AudioSampleFormat sampleFormat, AudioStorageLayout layout,
            params string[] channels)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentNullException.ThrowIfNull(channels);
            if (channels.Length == 0 || channels.Any(string.IsNullOrWhiteSpace) ||
                channels.Distinct(StringComparer.Ordinal).Count() != channels.Length)
            {
                throw new ArgumentException("A nonempty, unique channel order is required.", nameof(channels));
            }
            SampleRate = sampleRate;
            SampleFormat = sampleFormat;
            Layout = layout;
            BytesPerSample = sampleFormat switch
            {
                AudioSampleFormat.Unsigned8 => 1,
                AudioSampleFormat.Signed16 => 2,
                AudioSampleFormat.Signed32 => 4,
                AudioSampleFormat.Float32 => 4,
                AudioSampleFormat.Float64 => 8,
                _ => throw new ArgumentOutOfRangeException(nameof(sampleFormat))
            };
            PlaneCount = layout switch
            {
                AudioStorageLayout.Interleaved => 1,
                AudioStorageLayout.Planar => channels.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(layout))
            };
            Channels = Array.AsReadOnly((string[])channels.Clone());
        }

        public int SampleRate { get; }
        public AudioSampleFormat SampleFormat { get; }
        public AudioStorageLayout Layout { get; }
        public IReadOnlyList<string> Channels { get; }
        public int BytesPerSample { get; }
        public int PlaneCount { get; }

        public int GetBufferSize(int samplesPerChannel)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerChannel);
            return checked(samplesPerChannel * Channels.Count * BytesPerSample);
        }
    }
}
