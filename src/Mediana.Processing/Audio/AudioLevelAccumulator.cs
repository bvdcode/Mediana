// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    /// <summary>Emits fixed-size mono RMS/peak windows without retaining frames or samples.
    /// Complete emits the final partial window. No gain normalization, tapering or display shaping is applied.
    /// The emitter is synchronous. Cancellation, emitter failure or invalid input makes the accumulator terminal;
    /// previously emitted windows remain a prefix of the incomplete result.</summary>
    public class AudioLevelAccumulator
    {
        private readonly int samplesPerWindow;
        private AudioFormat? format;
        private AudioSampleClock? clock;
        private long samples;
        private int count;
        private double scale;
        private double squares;
        private bool completed;
        private bool faulted;
        private int busy;

        public AudioLevelAccumulator(int samplesPerWindow)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerWindow);
            this.samplesPerWindow = samplesPerWindow;
        }

        public void Process(AudioFrame input, Action<AudioLevel> emit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(emit);
            Enter();
            bool succeeded = false;
            try
            {
                RequireActive();
                if (completed)
                {
                    throw new InvalidOperationException("The audio level accumulator is complete.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (input.Format.Channels.Count != 1)
                {
                    throw new NotSupportedException("Audio levels require an explicit mono mix.");
                }
                if (format is null)
                {
                    format = input.Format;
                    clock = new AudioSampleClock(input.Timestamp, format.SampleRate, format.SampleRate);
                }
                PcmSampleAccess.RequireFormat(input.Format, format);
                clock!.Validate(input.Timestamp, samples);
                for (int index = 0; index < input.SamplesPerChannel; index++)
                {
                    if ((index & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    double magnitude = Math.Abs(PcmSampleAccess.Read(input, index, 0));
                    if (magnitude > scale)
                    {
                        double ratio = scale / magnitude;
                        squares = 1 + squares * ratio * ratio;
                        scale = magnitude;
                    }
                    else if (magnitude != 0)
                    {
                        double ratio = magnitude / scale;
                        squares += ratio * ratio;
                    }
                    samples = checked(samples + 1);
                    count++;
                    if (count == samplesPerWindow)
                    {
                        Emit(emit);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                }
                Volatile.Write(ref busy, 0);
            }
        }

        public void Complete(Action<AudioLevel> emit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(emit);
            Enter();
            bool succeeded = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (!completed && count != 0)
                {
                    Emit(emit);
                }
                cancellationToken.ThrowIfCancellationRequested();
                completed = true;
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                }
                Volatile.Write(ref busy, 0);
            }
        }

        private void Emit(Action<AudioLevel> emit)
        {
            long start = samples - count;
            double rms = scale * Math.Sqrt(squares / count);
            emit(new AudioLevel(start, count, clock!.AtOutput(start),
                new MediaDuration(count, new TimeBase(1, format!.SampleRate)), rms, scale));
            count = 0;
            scale = 0;
            squares = 0;
        }

        private void RequireActive()
        {
            if (faulted)
            {
                throw new InvalidOperationException("The audio level accumulator has failed.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent audio level operations are not supported.");
            }
        }
    }
}
