// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Containers.Wave
{
    /// <summary>Streams PCM frames into RIFF/WAVE, borrowing frames, allocator and an empty seekable destination.
    /// CompleteAsync finalizes sizes; unfinished or failed output must be discarded. RF64 is not supported.</summary>
    public class WavePcmWriter : IAsyncDisposable
    {
        private readonly Stream destination;
        private readonly AudioFormat format;
        private readonly MediaBufferAllocator allocator;
        private readonly byte[] header;
        private readonly bool floating;
        private long dataBytes;
        private int busy;
        private bool completed;
        private bool faulted;
        private bool disposed;

        private WavePcmWriter(Stream destination, AudioFormat format, MediaBufferAllocator allocator, byte[] header, bool floating)
        {
            this.destination = destination;
            this.format = format;
            this.allocator = allocator;
            this.header = header;
            this.floating = floating;
        }

        public long SamplesPerChannel => dataBytes / (format.Channels.Count * format.BytesPerSample);

        public static async ValueTask<WavePcmWriter> OpenAsync(Stream destination, AudioFormat format,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (!destination.CanWrite || !destination.CanSeek || destination.Position != 0 || destination.Length != 0)
            {
                throw new ArgumentException("The WAVE destination must be empty, writable and seekable.", nameof(destination));
            }
            byte[] configuration = PcmConfiguration.WriteWaveFormat(format);
            bool floating = format.SampleFormat switch
            {
                AudioSampleFormat.Unsigned8 or AudioSampleFormat.Signed16 or AudioSampleFormat.Signed32 => false,
                AudioSampleFormat.Float32 or AudioSampleFormat.Float64 => true,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            byte[] header = new byte[28 + configuration.Length + (floating ? 12 : 0)];
            "RIFF"u8.CopyTo(header);
            "WAVEfmt "u8.CopyTo(header.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)configuration.Length);
            configuration.CopyTo(header.AsSpan(20));
            if (floating)
            {
                "fact"u8.CopyTo(header.AsSpan(20 + configuration.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24 + configuration.Length), 4);
            }
            "data"u8.CopyTo(header.AsSpan(header.Length - 8));
            await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            return new(destination, format, allocator, header, floating);
        }

        public async ValueTask WriteFrameAsync(AudioFrame frame, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureWritable();
                ArgumentNullException.ThrowIfNull(frame);
                cancellationToken.ThrowIfCancellationRequested();
                AudioFormat actual = frame.Format;
                if (actual.SampleRate != format.SampleRate || actual.SampleFormat != format.SampleFormat ||
                    actual.Layout != format.Layout || !actual.Channels.SequenceEqual(format.Channels))
                {
                    throw new NotSupportedException("WAVE frames must retain their configured PCM format and channel order.");
                }
                ReadOnlyMemory<byte> samples = frame.GetPlane(0);
                long next = checked(dataBytes + samples.Length);
                if (next + (next & 1) + header.Length - 8 > uint.MaxValue)
                {
                    throw new NotSupportedException("The output exceeds RIFF/WAVE's 32-bit size limit; RF64 is not supported.");
                }
                if (BitConverter.IsLittleEndian || format.BytesPerSample == 1)
                {
                    await destination.WriteAsync(samples, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using WritableMediaBuffer ordered = allocator.Allocate(samples.Length);
                    for (int offset = 0; offset < samples.Length; offset += format.BytesPerSample)
                    {
                        if ((offset & 4095) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        Span<byte> value = ordered.Memory.Span.Slice(offset, format.BytesPerSample);
                        samples.Span.Slice(offset, format.BytesPerSample).CopyTo(value);
                        value.Reverse();
                    }
                    await destination.WriteAsync(ordered.Memory, cancellationToken).ConfigureAwait(false);
                }
                dataBytes = next;
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureWritable();
                cancellationToken.ThrowIfCancellationRequested();
                if ((dataBytes & 1) != 0)
                {
                    await destination.WriteAsync(new byte[1], cancellationToken).ConfigureAwait(false);
                }
                long length = header.Length + dataBytes + (dataBytes & 1);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)(length - 8)));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(header.Length - 4), (uint)dataBytes);
                if (floating)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(header.Length - 12), (uint)SamplesPerChannel);
                }
                destination.Position = 0;
                await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                destination.Position = length;
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                completed = true;
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                Enter();
                disposed = true;
                Volatile.Write(ref busy, 0);
            }
            return ValueTask.CompletedTask;
        }

        private void Enter()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent WAVE writer operations are not supported.");
            }
        }

        private void EnsureWritable()
        {
            if (completed || faulted)
            {
                throw new InvalidOperationException("The WAVE writer is completed or faulted.");
            }
        }
    }
}
