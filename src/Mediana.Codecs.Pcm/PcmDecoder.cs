// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Pcm
{
    internal class PcmDecoder(int streamIndex, PcmConfiguration configuration, MediaBufferAllocator allocator,
        int maxSamplesPerFrame) : IMediaCodec<MediaPacket, AudioFrame>
    {
        private AudioFrame? pending;
        private bool completed;
        private bool disposed;
        private bool faulted;

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
                    throw new InvalidOperationException("PCM input is complete.");
                }
                if (pending is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty || input.Data.Length % configuration.BlockAlignment != 0)
                {
                    throw new InvalidDataException("The PCM packet does not contain complete blocks for the selected stream.");
                }
                int samples = input.Data.Length / configuration.BlockAlignment;
                if (samples > maxSamplesPerFrame)
                {
                    throw new NotSupportedException("The PCM packet exceeds the configured sample limit.");
                }
                using WritableMediaBuffer writable = allocator.Allocate(configuration.Format.GetBufferSize(samples));
                ConvertSamples(input.Data.Span, writable.Memory.Span, cancellationToken);
                using MediaBuffer buffer = writable.Publish();
                pending = new AudioFrame(configuration.Format, samples, buffer, input.PresentationTime);
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

        private void ConvertSamples(ReadOnlySpan<byte> source, Span<byte> destination, CancellationToken cancellationToken)
        {
            int sourceBytes = configuration.SourceBitsPerSample / 8;
            int targetBytes = configuration.Format.BytesPerSample;
            int padding = configuration.SourceBitsPerSample - configuration.ValidBitsPerSample;
            ulong paddingMask = (1UL << padding) - 1;
            for (int offset = 0, output = 0; offset < source.Length; offset += sourceBytes, output += targetBytes)
            {
                if ((offset & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                ReadOnlySpan<byte> input = source.Slice(offset, sourceBytes);
                Span<byte> sample = destination.Slice(output, targetBytes);
                switch (configuration.Format.SampleFormat)
                {
                    case AudioSampleFormat.Unsigned8:
                        ValidatePadding(input[0], paddingMask);
                        sample[0] = input[0];
                        break;
                    case AudioSampleFormat.Signed16:
                        ushort word = BinaryPrimitives.ReadUInt16LittleEndian(input);
                        ValidatePadding(word, paddingMask);
                        MemoryMarshal.Write(sample, in word);
                        break;
                    case AudioSampleFormat.Signed32:
                        uint integer = sourceBytes switch
                        {
                            3 => (uint)(input[0] | input[1] << 8 | input[2] << 16),
                            4 => BinaryPrimitives.ReadUInt32LittleEndian(input),
                            _ => throw new InvalidOperationException("The PCM integer storage size is invalid.")
                        };
                        ValidatePadding(integer, paddingMask);
                        integer <<= (4 - sourceBytes) * 8;
                        MemoryMarshal.Write(sample, in integer);
                        break;
                    case AudioSampleFormat.Float32:
                        uint single = BinaryPrimitives.ReadUInt32LittleEndian(input);
                        MemoryMarshal.Write(sample, in single);
                        break;
                    case AudioSampleFormat.Float64:
                        ulong number = BinaryPrimitives.ReadUInt64LittleEndian(input);
                        MemoryMarshal.Write(sample, in number);
                        break;
                    default:
                        throw new InvalidOperationException("The PCM sample format is invalid.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static void ValidatePadding(ulong sample, ulong mask)
        {
            if ((sample & mask) != 0)
            {
                throw new InvalidDataException("Unused PCM precision bits must be zero.");
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
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose();
            pending = null;
            completed = false;
            faulted = false;
        }

        private void Fault()
        {
            pending?.Dispose();
            pending = null;
            faulted = true;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The PCM decoder has failed and requires reset or disposal.");
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
