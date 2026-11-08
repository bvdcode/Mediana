// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Adts
{
    /// <summary>Forward-only ADTS reader, one raw data block per frame. Borrows its source.
    /// Opening reads only the first header. Packets exclude transport headers and optional CRC words;
    /// CRC values are not verified. Midstream configuration changes and resynchronization are not supported.</summary>
    public class AdtsReader : IContainerReader
    {
        private readonly IByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer scratch;
        private AdtsFrameHeader? next;
        private long frames;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        private AdtsReader(IByteSource source, MediaBufferAllocator allocator)
        {
            this.source = source;
            this.allocator = allocator;
            scratch = allocator.Allocate(AdtsFrameHeader.ByteLength + 2);
        }

        public AdtsFrameHeader FirstFrame { get; private set; }
        public ContainerInfo Info { get; } = new("adts");
        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];
        /// <summary>Known only after EOF. Includes the decoder's priming and final padding.</summary>
        public MediaDuration? CodedDuration => ended ? new(checked(frames * AdtsFrameValidation.SamplesPerFrame), Streams[0].TimeBase) : null;

        public static async ValueTask<AdtsReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            AdtsReader reader = new(source, allocator);
            bool success = false;
            try
            {
                await source.ReadExactlyAsync(reader.scratch.Memory[..AdtsFrameHeader.ByteLength], cancellationToken).ConfigureAwait(false);
                reader.FirstFrame = AdtsFrameHeader.Read(reader.scratch.Memory.Span);
                AdtsFrameValidation.RequireSingleBlock(reader.FirstFrame);
                reader.next = reader.FirstFrame;
                reader.Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, AacConfiguration.CodecId,
                    StreamFormats.Aac, new TimeBase(1, reader.FirstFrame.SampleRate), reader.FirstFrame.ToAudioSpecificConfig()) });
                success = true;
                return reader;
            }
            finally { if (!success) { await reader.DisposeAsync().ConfigureAwait(false); } }
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (ended) { success = true; return null; }
                AdtsFrameHeader? header = next ?? await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
                if (header is null) { ended = true; success = true; return null; }
                AdtsFrameValidation.RequireConfiguration(FirstFrame, header.Value);
                if (header.Value.HasCrc)
                {
                    await source.ReadExactlyAsync(scratch.Memory[..2], cancellationToken).ConfigureAwait(false);
                }
                using WritableMediaBuffer writable = allocator.Allocate(header.Value.FrameBytes - header.Value.HeaderBytes);
                await source.ReadExactlyAsync(writable.Memory, cancellationToken).ConfigureAwait(false);
                MediaTimestamp timestamp = new(checked(frames * AdtsFrameValidation.SamplesPerFrame), Streams[0].TimeBase);
                using MediaBuffer buffer = writable.Publish();
                MediaPacket packet = new(0, buffer, timestamp, timestamp, new MediaDuration(AdtsFrameValidation.SamplesPerFrame, timestamp.TimeBase));
                next = null;
                frames++;
                success = true;
                return packet;
            }
            finally
            {
                if (!success) { faulted = true; scratch.Dispose(); }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<AdtsFrameHeader?> ReadHeaderAsync(CancellationToken token)
        {
            int count = await source.ReadAsync(scratch.Memory[..AdtsFrameHeader.ByteLength], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (count is < 0 or > AdtsFrameHeader.ByteLength)
            {
                throw new InvalidDataException("The ADTS source returned an invalid read count.");
            }
            if (count == 0) { return null; }
            await source.ReadExactlyAsync(scratch.Memory.Slice(count, AdtsFrameHeader.ByteLength - count), token).ConfigureAwait(false);
            return AdtsFrameHeader.Read(scratch.Memory.Span);
        }

        private void RequireActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The ADTS reader has failed and must be disposed."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent ADTS reader operations are not supported.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; scratch.Dispose(); }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
