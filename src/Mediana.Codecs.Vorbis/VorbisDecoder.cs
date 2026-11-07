// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisDecoder(int streamIndex, VorbisConfiguration configuration, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private readonly AudioFormat format = new(configuration.Identification.SampleRate, AudioSampleFormat.Float32,
            AudioStorageLayout.Interleaved, VorbisChannelOrder.Create(configuration.Identification.ChannelCount));
        private VorbisFrameDecoder? workspace;
        private AudioFrame? pending;
        private long prerollPackets;
        private bool disposed, faulted, completed;

        public int RequiredPrerollPackets => 1;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input); EnsureUsable();
            bool success = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("Vorbis input is complete."); }
                if (pending is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (input.StreamIndex != streamIndex) { throw new InvalidDataException("The Vorbis packet belongs to a different stream."); }
                workspace ??= new(configuration.Identification, configuration.SetupHeader.Span, allocator, cancellationToken);
                int samples = workspace.GetOutputSamples(input.Data.Span);
                if (samples == 0)
                {
                    workspace.Decode(input.Data.Span, [], cancellationToken);
                }
                else
                {
                    int count = AudioPacketTiming.GetPresentationSamples(input.Duration, format.SampleRate, samples);
                    using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(samples));
                    workspace.Decode(input.Data.Span, MemoryMarshal.Cast<byte, float>(writable.Span), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (prerollPackets == 0 && count > 0)
                    {
                        using MediaBuffer buffer = writable.Publish();
                        using MediaBuffer view = buffer.Slice(0, format.GetBufferSize(count));
                        pending = new(format, count, view, input.PresentationTime);
                    }
                }
                if (prerollPackets > 0) { prerollPackets--; }
                success = true;
                return CodecInputStatus.Accepted;
            }
            finally { if (!success) { Fault(); } }
        }

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested) { Fault(); cancellationToken.ThrowIfCancellationRequested(); }
            if (pending is not null)
            {
                AudioFrame frame = pending; pending = null; return CodecOutput<AudioFrame>.Available(frame);
            }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            EnsureUsable();
            if (prerollPackets != 0) { Fault(); throw new InvalidDataException("Vorbis input ended before its requested preroll was complete."); }
            completed = true;
        }

        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount);
            Reset(); prerollPackets = packetCount;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose(); pending = null; workspace?.Reset();
            prerollPackets = 0; completed = false; faulted = false;
        }

        private void Fault() { Reset(); faulted = true; }
        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The Vorbis decoder has failed and requires reset or disposal."); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            pending?.Dispose(); pending = null; workspace?.Dispose(); workspace = null; disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
