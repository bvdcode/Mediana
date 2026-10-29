using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacDecoder : IMediaCodec<MediaPacket, AudioFrame>
    {
        private const uint InitialNoiseState = 0x13579bdf;
        private readonly int streamIndex;
        private readonly AacConfiguration configuration;
        private readonly MediaBufferAllocator allocator;
        private readonly int maxAccessUnitBytes;
        private readonly AudioFormat format;
        private AacDecoderWorkspace? workspace;
        private AudioFrame? pending;
        private uint noiseState = InitialNoiseState;
        private bool completed;
        private bool disposed;
        private bool faulted;

        public AacDecoder(int streamIndex, AacConfiguration configuration, MediaBufferAllocator allocator, int maxAccessUnitBytes)
        {
            this.streamIndex = streamIndex;
            this.configuration = configuration;
            this.allocator = allocator;
            this.maxAccessUnitBytes = maxAccessUnitBytes;
            string[] channels = configuration.ChannelConfiguration switch
            {
                1 => ["front-center"],
                2 => ["front-left", "front-right"],
                _ => throw new NotSupportedException("The AAC channel layout is not supported.")
            };
            format = new AudioFormat(configuration.SampleRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, channels);
        }

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            EnsureUsable();
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("AAC input is complete.");
                }
                if (pending is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty)
                {
                    throw new InvalidDataException("The AAC packet is empty or belongs to a different stream.");
                }
                if (input.Data.Length > maxAccessUnitBytes)
                {
                    throw new NotSupportedException("The AAC packet exceeds the configured access unit limit.");
                }
                workspace ??= new AacDecoderWorkspace(allocator, configuration.ChannelCount);
                AacAccessUnitParser.Read(input.Data.Span, configuration.FrequencyIndex, workspace, ref noiseState, cancellationToken);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(1024));
                Span<float> output = MemoryMarshal.Cast<byte, float>(writable.Memory.Span);
                Synthesize(workspace.Left, output, 0, cancellationToken);
                if (workspace.Right is not null)
                {
                    Synthesize(workspace.Right, output, 1, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer buffer = writable.Publish();
                pending = new AudioFrame(format, 1024, buffer, input.PresentationTime);
                succeeded = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!succeeded)
                {
                    Fault();
                }
            }
        }

        private void Synthesize(AacChannelState channel, Span<float> output, int channelIndex, CancellationToken cancellationToken)
        {
            AacDecoderWorkspace current = workspace ?? throw new InvalidOperationException("AAC workspace is missing.");
            Span<double> synthesized = current.Output;
            AacSynthesis.Process(channel.Spectrum, channel.Info.Sequence, channel.Info.Shape, channel.PreviousShape,
                channel.Overlap, synthesized, current.Time, current.Fourier, cancellationToken);
            channel.PreviousShape = channel.Info.Shape;
            for (int index = 0; index < 1024; index++)
            {
                float value = (float)synthesized[index];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("AAC synthesis produced a nonfinite PCM sample.");
                }
                output[index * format.Channels.Count + channelIndex] = value;
            }
        }

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested)
            {
                Fault();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (pending is not null)
            {
                AudioFrame frame = pending;
                pending = null;
                return CodecOutput<AudioFrame>.Available(frame);
            }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            EnsureUsable();
            completed = true;
            workspace?.Dispose();
            workspace = null;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose();
            pending = null;
            workspace?.Dispose();
            workspace = null;
            noiseState = InitialNoiseState;
            completed = false;
            faulted = false;
        }

        private void Fault()
        {
            Reset();
            faulted = true;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The AAC decoder has failed and requires reset or disposal.");
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Reset();
                disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
