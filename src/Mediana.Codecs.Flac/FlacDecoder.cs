// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Flac
{
    internal class FlacDecoder : IMediaCodec<MediaPacket, AudioFrame>
    {
        private readonly int streamIndex;
        private readonly FlacConfiguration configuration;
        private readonly MediaBufferAllocator allocator;
        private readonly int maxFrameBytes;
        private readonly AudioFormat format;
        private readonly FlacSubframeDecoder subframe = new();
        private WritableMediaBuffer? encoded;
        private WritableMediaBuffer? workspace;
        private MediaPacket? packet;
        private int packetOffset;
        private int used;
        private int bitPosition;
        private int channel;
        private FlacFrameHeader? header;
        private bool? variable;
        private bool lastBlockOnly;
        private long frames;
        private long samples;
        private bool completed;
        private bool disposed;
        private bool faulted;

        public FlacDecoder(int streamIndex, FlacConfiguration configuration, MediaBufferAllocator allocator, int maxFrameBytes)
        {
            this.streamIndex = streamIndex;
            this.configuration = configuration;
            this.allocator = allocator;
            this.maxFrameBytes = configuration.MaximumFrameBytes == 0
                ? maxFrameBytes : Math.Min(maxFrameBytes, configuration.MaximumFrameBytes);
            string[] channels = configuration.ChannelCount switch
            {
                1 => ["front-center"],
                2 => ["front-left", "front-right"],
                3 => ["front-left", "front-right", "front-center"],
                4 => ["front-left", "front-right", "back-left", "back-right"],
                5 => ["front-left", "front-right", "front-center", "back-left", "back-right"],
                6 => ["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right"],
                7 => ["front-left", "front-right", "front-center", "low-frequency", "back-center", "side-left", "side-right"],
                8 => ["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right", "side-left", "side-right"],
                _ => throw new InvalidDataException("The FLAC channel count is invalid.")
            };
            format = new AudioFormat(configuration.SampleRate, AudioSampleFormat.Signed32, AudioStorageLayout.Interleaved, channels);
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
                    throw new InvalidOperationException("FLAC input is complete.");
                }
                if (packet is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty)
                {
                    throw new InvalidDataException("A nonempty FLAC packet for the selected stream is required.");
                }
                packet = input.Retain();
                packetOffset = 0;
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

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (true)
                {
                    AudioFrame? frame = TryDecode(cancellationToken);
                    if (frame is not null)
                    {
                        succeeded = true;
                        return CodecOutput<AudioFrame>.Available(frame);
                    }
                    if (packet is not null)
                    {
                        AppendPacket();
                        continue;
                    }
                    if (!completed)
                    {
                        succeeded = true;
                        return CodecOutput<AudioFrame>.InputRequired;
                    }
                    if (used != 0 || frames == 0 || (configuration.TotalSamples != 0 && samples != configuration.TotalSamples))
                    {
                        throw new InvalidDataException("The FLAC stream is truncated or its total sample count does not match.");
                    }
                    ReleaseBuffers();
                    succeeded = true;
                    return CodecOutput<AudioFrame>.EndOfStream;
                }
            }
            finally
            {
                if (!succeeded)
                {
                    Fault();
                }
            }
        }

        private void AppendPacket()
        {
            MediaPacket input = packet ?? throw new InvalidOperationException();
            if (encoded is null)
            {
                encoded = allocator.Allocate(Math.Min(4096, maxFrameBytes));
            }
            if (used == encoded.Memory.Length)
            {
                if (used == maxFrameBytes)
                {
                    throw new NotSupportedException("A FLAC frame exceeds the configured or STREAMINFO byte limit.");
                }
                WritableMediaBuffer replacement = allocator.Allocate((int)Math.Min((long)maxFrameBytes, (long)used * 2));
                bool transferred = false;
                try
                {
                    encoded.Memory.Span[..used].CopyTo(replacement.Memory.Span);
                    encoded.Dispose();
                    encoded = replacement;
                    transferred = true;
                }
                finally
                {
                    if (!transferred)
                    {
                        replacement.Dispose();
                    }
                }
            }
            int count = Math.Min(input.Data.Length - packetOffset, encoded.Memory.Length - used);
            input.Data.Span.Slice(packetOffset, count).CopyTo(encoded.Memory.Span[used..]);
            used += count;
            packetOffset += count;
            if (packetOffset == input.Data.Length)
            {
                input.Dispose();
                packet = null;
            }
        }

        private AudioFrame? TryDecode(CancellationToken cancellationToken)
        {
            if (used == 0)
            {
                return null;
            }
            ReadOnlySpan<byte> data = encoded!.Memory.Span[..used];
            if (header is null)
            {
                if (!FlacFrameHeader.TryRead(data, configuration, out FlacFrameHeader parsed))
                {
                    return null;
                }
                ValidateHeader(parsed);
                header = parsed;
                bitPosition = parsed.ByteLength * 8;
                workspace = allocator.Allocate(checked(parsed.BlockSize * configuration.ChannelCount * sizeof(long)));
            }
            FlacFrameHeader current = header.Value;
            Span<long> decoded = MemoryMarshal.Cast<byte, long>(workspace!.Memory.Span);
            FlacBitReader reader = new(data, bitPosition);
            while (channel < configuration.ChannelCount)
            {
                int precision = configuration.BitsPerSample;
                if ((current.ChannelAssignment is 8 or 10 && channel == 1) || (current.ChannelAssignment == 9 && channel == 0))
                {
                    precision++;
                }
                bool ready = subframe.TryRead(ref reader, decoded.Slice(channel * current.BlockSize, current.BlockSize),
                    precision, cancellationToken);
                bitPosition = reader.Position;
                if (!ready)
                {
                    return null;
                }
                channel++;
                subframe.Reset();
            }
            int padding = (8 - (reader.Position & 7)) & 7;
            if (!reader.TryRead(padding, out ulong paddingBits) || !reader.TryRead(16, out ulong crc))
            {
                return null;
            }
            if (paddingBits != 0 || crc != FlacCrc.Frame(data[..(reader.Position / 8 - 2)], cancellationToken))
            {
                throw new InvalidDataException("The FLAC frame padding or CRC is invalid.");
            }
            int frameBytes = reader.Position / 8;
            if (frameBytes < configuration.MinimumFrameBytes)
            {
                throw new InvalidDataException("The FLAC frame is smaller than STREAMINFO declares.");
            }
            using WritableMediaBuffer output = allocator.Allocate(format.GetBufferSize(current.BlockSize));
            WriteOutput(current, decoded, MemoryMarshal.Cast<byte, int>(output.Memory.Span), cancellationToken);
            using MediaBuffer buffer = output.Publish();
            AudioFrame result = new(format, current.BlockSize, buffer, new MediaTimestamp(samples, new TimeBase(1, configuration.SampleRate)));
            samples += current.BlockSize;
            frames++;
            lastBlockOnly = current.BlockSize < configuration.MinimumBlockSize
                || (!current.VariableBlockSize && current.BlockSize != configuration.MaximumBlockSize);
            data[frameBytes..].CopyTo(encoded.Memory.Span);
            used -= frameBytes;
            workspace.Dispose();
            workspace = null;
            header = null;
            channel = 0;
            bitPosition = 0;
            return result;
        }

        private void ValidateHeader(FlacFrameHeader current)
        {
            if (lastBlockOnly || (variable is not null && variable != current.VariableBlockSize)
                || current.Number != (current.VariableBlockSize ? samples : frames))
            {
                throw new InvalidDataException("The FLAC blocking strategy, frame/sample sequence or final block size is invalid.");
            }
            if (!current.VariableBlockSize && configuration.MinimumBlockSize != configuration.MaximumBlockSize)
            {
                throw new InvalidDataException("A fixed-block FLAC stream requires equal STREAMINFO block sizes.");
            }
            if (samples > 0xfffffffffL - current.BlockSize
                || (configuration.TotalSamples != 0 && current.BlockSize > configuration.TotalSamples - samples))
            {
                throw new InvalidDataException("The FLAC frame exceeds the stream sample count.");
            }
            variable = current.VariableBlockSize;
        }

        private void WriteOutput(FlacFrameHeader current, ReadOnlySpan<long> input, Span<int> output, CancellationToken cancellationToken)
        {
            for (int i = 0; i < current.BlockSize; i++)
            {
                if ((i & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (current.ChannelAssignment < 8)
                {
                    for (int c = 0; c < configuration.ChannelCount; c++)
                    {
                        output[i * configuration.ChannelCount + c] = ConvertSample(input[c * current.BlockSize + i]);
                    }
                    continue;
                }
                long first = input[i];
                long second = input[current.BlockSize + i];
                long left;
                long right;
                switch (current.ChannelAssignment)
                {
                    case 8:
                        left = first;
                        right = first - second;
                        break;
                    case 9:
                        left = first + second;
                        right = second;
                        break;
                    case 10:
                        long mid = (first << 1) | (second & 1);
                        left = (mid + second) >> 1;
                        right = (mid - second) >> 1;
                        break;
                    default:
                        throw new InvalidOperationException("The FLAC stereo assignment is invalid.");
                }
                output[2 * i] = ConvertSample(left);
                output[2 * i + 1] = ConvertSample(right);
            }
        }

        private int ConvertSample(long sample)
        {
            FlacSubframeDecoder.ValidatePrecision(sample, configuration.BitsPerSample);
            return (int)(sample << (32 - configuration.BitsPerSample));
        }

        public void CompleteInput()
        {
            EnsureUsable();
            completed = true;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ReleaseBuffers();
            completed = false;
            faulted = false;
            variable = null;
            lastBlockOnly = false;
            samples = 0;
            frames = 0;
        }

        private void ReleaseBuffers()
        {
            packet?.Dispose();
            encoded?.Dispose();
            workspace?.Dispose();
            packet = null;
            encoded = null;
            workspace = null;
            header = null;
            used = 0;
            bitPosition = 0;
            channel = 0;
            packetOffset = 0;
            subframe.Reset();
        }

        private void Fault()
        {
            ReleaseBuffers();
            faulted = true;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The FLAC decoder has failed and requires reset or disposal.");
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                ReleaseBuffers();
                disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
