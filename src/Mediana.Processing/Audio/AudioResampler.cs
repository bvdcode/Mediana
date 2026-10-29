using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    /// <summary>Pull-driven Blackman-windowed sinc conversion to interleaved float64.
    /// The callback transfers ownership of each input frame; the callback's source remains borrowed.
    /// Uses a bounded history and exact rational sample positions. Boundary extension is explicit;
    /// output has ceil(input samples * output rate / input rate) samples with compensated filter delay.
    /// Input format and timestamps must remain continuous. Equal rates preserve samples without filtering.</summary>
    public class AudioResampler : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<AudioFrame?>> readFrame;
        private readonly int outputRate;
        private readonly MediaBufferAllocator allocator;
        private readonly AudioResampleOptions options;
        private AudioFrame? input;
        private AudioFormat? inputFormat;
        private AudioFormat? outputFormat;
        private AudioSampleClock? clock;
        private SincFilterBank? filter;
        private WritableMediaBuffer? history;
        private int inputOffset;
        private long inputSamples;
        private long outputSamples;
        private bool inputEnded;
        private bool drained;
        private bool disposed;
        private bool faulted;
        private int busy;

        public AudioResampler(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame, int outputRate,
            MediaBufferAllocator allocator, AudioResampleOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(readFrame);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);
            ArgumentNullException.ThrowIfNull(allocator);
            this.readFrame = readFrame;
            this.outputRate = outputRate;
            this.allocator = allocator;
            this.options = options ?? new AudioResampleOptions();
            this.options.Validate();
        }

        public async ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The audio resampler has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                AudioFrame? result = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return result;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                    ReleaseStorage();
                }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<AudioFrame?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            if (drained)
            {
                return null;
            }
            if (inputFormat is null)
            {
                input = await readFrame(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (input is null)
                {
                    drained = true;
                    return null;
                }
                inputFormat = input.Format;
                outputFormat = new AudioFormat(outputRate, AudioSampleFormat.Float64, AudioStorageLayout.Interleaved,
                    inputFormat.Channels.ToArray());
                clock = new AudioSampleClock(input.Timestamp, inputFormat.SampleRate, outputRate);
                filter = SincFilterBank.Create(inputFormat.SampleRate, outputRate, options, allocator, cancellationToken);
                history = allocator.Allocate(checked(filter.Taps * inputFormat.Channels.Count * sizeof(double)));
            }
            using WritableMediaBuffer output = allocator.Allocate(outputFormat!.GetBufferSize(options.SamplesPerFrame));
            long start = outputSamples;
            int count = 0;
            while (count < options.SamplesPerFrame)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int128 position = (Int128)outputSamples * inputFormat.SampleRate;
                long center = checked((long)(position / outputRate));
                int phase = (int)(position % outputRate) / AudioSampleClock.GreatestCommonDivisor(inputFormat.SampleRate, outputRate);
                await FillAsync(checked(center + filter!.Radius), cancellationToken).ConfigureAwait(false);
                if (inputEnded && outputSamples >= OutputLength())
                {
                    break;
                }
                Convolve(output.Memory.Span[(count * outputFormat.Channels.Count * sizeof(double))..], center, phase, cancellationToken);
                outputSamples = checked(outputSamples + 1);
                count++;
            }
            MediaTimestamp? timestamp = clock!.AtOutput(start);
            if (inputEnded && outputSamples >= OutputLength())
            {
                drained = true;
                ReleaseStorage();
            }
            if (count == 0)
            {
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer complete = output.Publish();
            using MediaBuffer buffer = complete.Slice(0, outputFormat.GetBufferSize(count));
            return new AudioFrame(outputFormat, count, buffer, timestamp);
        }

        private async ValueTask FillAsync(long lastSample, CancellationToken cancellationToken)
        {
            while (!inputEnded && inputSamples <= lastSample)
            {
                if (input is null)
                {
                    input = await readFrame(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (input is null)
                    {
                        inputEnded = true;
                        break;
                    }
                    PcmSampleAccess.RequireFormat(input.Format, inputFormat!);
                    clock!.Validate(input.Timestamp, inputSamples);
                    inputOffset = 0;
                }
                cancellationToken.ThrowIfCancellationRequested();
                int channels = inputFormat!.Channels.Count;
                int index = (int)(inputSamples % filter!.Taps) * channels;
                Span<double> ring = MemoryMarshal.Cast<byte, double>(history!.Memory.Span);
                for (int channel = 0; channel < channels; channel++)
                {
                    ring[index + channel] = PcmSampleAccess.Read(input, inputOffset, channel);
                }
                inputSamples = checked(inputSamples + 1);
                inputOffset++;
                if (inputOffset == input.SamplesPerChannel)
                {
                    input.Dispose();
                    input = null;
                }
            }
        }

        private void Convolve(Span<byte> output, long center, int phase, CancellationToken cancellationToken)
        {
            ReadOnlySpan<double> weights = filter!.GetPhase(phase);
            ReadOnlySpan<double> ring = MemoryMarshal.Cast<byte, double>(history!.Memory.Span);
            int channels = inputFormat!.Channels.Count;
            for (int channel = 0; channel < channels; channel++)
            {
                double value = 0;
                for (int tap = 0; tap < filter.Taps; tap++)
                {
                    if ((tap & 1023) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    long sample = center - filter.Radius + tap;
                    if (sample < 0 || sample >= inputSamples)
                    {
                        sample = AudioBoundaryExtension.Resolve(sample, inputSamples, options.BoundaryMode);
                    }
                    if (sample >= 0)
                    {
                        value += ring[(int)(sample % filter.Taps) * channels + channel] * weights[tap];
                    }
                }
                PcmSampleAccess.Write(output[(channel * sizeof(double))..], AudioSampleFormat.Float64, value);
            }
        }

        private long OutputLength() => checked((long)(((Int128)inputSamples * outputRate + inputFormat!.SampleRate - 1) / inputFormat.SampleRate));

        private void ReleaseStorage()
        {
            input?.Dispose();
            input = null;
            history?.Dispose();
            history = null;
            filter?.Dispose();
            filter = null;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent audio resampler operations are not supported.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                ReleaseStorage();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
