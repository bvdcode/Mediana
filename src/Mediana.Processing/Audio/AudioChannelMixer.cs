using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    /// <summary>Applies an explicit output-by-input channel matrix. No implicit speaker mapping,
    /// gain normalization or clipping is applied. Output is interleaved float64 with preserved timing.</summary>
    public class AudioChannelMixer
    {
        private readonly double[] coefficients;

        public AudioChannelMixer(AudioFormat inputFormat, string[] outputChannels, ReadOnlySpan<double> coefficients)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(outputChannels);
            OutputFormat = new AudioFormat(inputFormat.SampleRate, AudioSampleFormat.Float64,
                AudioStorageLayout.Interleaved, outputChannels);
            if (coefficients.Length != checked(inputFormat.Channels.Count * outputChannels.Length))
            {
                throw new ArgumentException("The matrix must contain one coefficient per output/input channel pair.", nameof(coefficients));
            }
            foreach (double coefficient in coefficients)
            {
                if (!double.IsFinite(coefficient))
                {
                    throw new ArgumentException("Channel coefficients must be finite.", nameof(coefficients));
                }
            }
            InputFormat = inputFormat;
            this.coefficients = coefficients.ToArray();
        }

        public AudioFormat InputFormat { get; }
        public AudioFormat OutputFormat { get; }

        public AudioFrame Apply(AudioFrame input, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            PcmSampleAccess.RequireFormat(input.Format, InputFormat);
            cancellationToken.ThrowIfCancellationRequested();
            using WritableMediaBuffer writable = allocator.Allocate(OutputFormat.GetBufferSize(input.SamplesPerChannel));
            for (int sample = 0; sample < input.SamplesPerChannel; sample++)
            {
                if ((sample & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                for (int output = 0; output < OutputFormat.Channels.Count; output++)
                {
                    double value = 0;
                    for (int channel = 0; channel < InputFormat.Channels.Count; channel++)
                    {
                        value += PcmSampleAccess.Read(input, sample, channel)
                            * coefficients[output * InputFormat.Channels.Count + channel];
                    }
                    int offset = (sample * OutputFormat.Channels.Count + output) * sizeof(double);
                    PcmSampleAccess.Write(writable.Memory.Span[offset..], AudioSampleFormat.Float64, value);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(OutputFormat, input.SamplesPerChannel, buffer, input.Timestamp);
        }
    }
}
