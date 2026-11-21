// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Audio;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal class DtsDecoder(int streamIndex, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private DtsFrameDecoder? workspace;
        private AudioFormat? format;
        private int channelMode, lfeMode;
        private long prerollPackets;
        public int RequiredPrerollPackets => 3;
        private AudioFrame? output;

        private bool completed;
        private bool disposed;
        private bool faulted;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureUsable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("DTS input is complete.");
                }
                if (output is not null)
                {
                    success = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (packet.StreamIndex != streamIndex)
                {
                    throw new ArgumentException("The packet belongs to a different DTS stream.", nameof(packet));
                }
                DtsFrameHeader header = DtsFrameHeader.Read(packet.Data.Span);
                if (format is not null && (format.SampleRate != header.SampleRate ||
                    channelMode != header.ChannelMode || lfeMode != header.LfeMode))
                {
                    throw new NotSupportedException("DTS stream configuration changed without resetting the decoder.");
                }
                format ??= new(header.SampleRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, header.GetChannelOrder());
                channelMode = header.ChannelMode;
                lfeMode = header.LfeMode;
                int count = AudioPacketTiming.GetPresentationSamples(packet.Duration, header.SampleRate, header.SamplesPerFrame);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(header.SamplesPerFrame));
                workspace ??= new DtsFrameDecoder(allocator);
                workspace.Decode(packet.Data.Span, header, MemoryMarshal.Cast<byte, float>(writable.Span), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (prerollPackets > 0) { prerollPackets--; }
                else if (count != 0)
                {
                    using MediaBuffer buffer = writable.Publish();
                    using MediaBuffer view = buffer.Slice(0, format.GetBufferSize(count));
                    output = new(format, count, view, packet.PresentationTime);
                }
                success = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                try
                {
                    if (!success) { Fault(); }
                }
                finally { Volatile.Write(ref busy, 0); }
            }
        }

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureUsable();
                if (cancellationToken.IsCancellationRequested) { Fault(); }
                cancellationToken.ThrowIfCancellationRequested();
                if (output is not null)
                {
                    AudioFrame frame = output;
                    output = null;
                    return CodecOutput<AudioFrame>.Available(frame);
                }
                return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void CompleteInput()
        {
            Enter();
            try
            {
                EnsureUsable();
                if (prerollPackets != 0)
                {
                    Fault();
                    throw new InvalidDataException("DTS input ended before the requested preroll was complete.");
                }
                completed = true;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount);
            Enter();
            try
            {
                ResetCore();
                prerollPackets = packetCount;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void ResetCore()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            AudioFrame? previous = output;
            output = null;
            previous?.Dispose();
            workspace?.Reset();
            format = null;
            prerollPackets = 0;
            completed = false;
            faulted = false;
        }

        public void Reset()
        {
            Enter();
            try
            {
                ResetCore();
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                disposed = true;
                ReleaseResources();
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void Fault()
        {
            faulted = true;
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            AudioFrame? frame = output;
            DtsFrameDecoder? state = workspace;
            output = null;
            workspace = null;
            try { frame?.Dispose(); }
            finally { state?.Dispose(); }
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The DTS decoder requires reset or disposal after a failure."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent DTS codec operations are not supported."); }
        }
    }
}
