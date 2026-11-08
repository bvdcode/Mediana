// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal class Ac3Decoder(int streamIndex, bool enhanced, MediaBufferAllocator allocator, bool applyDynamicRangeCompression) : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private const int MaximumFramesPerAccessUnit = 6;
        private const int MaximumSamplesPerAccessUnit = 1536;
        private readonly Ac3FrameHeader?[] frames = new Ac3FrameHeader?[MaximumFramesPerAccessUnit];
        private Ac3FrameDecoder? workspace;
        private Ac3FrameHeader? configuration;
        private AudioFormat? format;
        private AudioFrame? pending;
        private long prerollPackets;
        private bool disposed, completed, faulted;
        public int RequiredPrerollPackets => 1;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input); EnsureUsable();
            bool success = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("AC-3 input is complete."); }
                if (pending is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (input.StreamIndex != streamIndex) { throw new InvalidDataException("The AC-3 packet belongs to a different stream."); }
                (int frameCount, int codedSamples) = ReadAccessUnit(input.Data.Span);
                Ac3FrameHeader header = frames[0]!;
                configuration ??= header;
                format ??= new(header.SampleRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, header.GetChannelOrder());
                workspace ??= new(allocator, applyDynamicRangeCompression);
                int count = AudioPacketTiming.GetPresentationSamples(input.Duration, header.SampleRate, codedSamples);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(codedSamples));
                Span<float> pcm = MemoryMarshal.Cast<byte, float>(writable.Span);
                for (int index = 0, offset = 0, samples = 0; index < frameCount; index++)
                {
                    Ac3FrameHeader frame = frames[index]!;
                    workspace.Decode(input.Data.Span.Slice(offset, frame.FrameBytes), frame,
                        pcm.Slice(samples * header.ChannelCount, frame.SamplesPerFrame * header.ChannelCount), cancellationToken);
                    offset += frame.FrameBytes; samples += frame.SamplesPerFrame;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (prerollPackets == 0 && count != 0)
                {
                    using MediaBuffer buffer = writable.Publish();
                    using MediaBuffer view = buffer.Slice(0, format.GetBufferSize(count));
                    pending = new(format, count, view, input.PresentationTime);
                }
                if (prerollPackets > 0) { prerollPackets--; }
                success = true; return CodecInputStatus.Accepted;
            }
            finally { if (!success) { Fault(); } }
        }

        private (int Frames, int Samples) ReadAccessUnit(ReadOnlySpan<byte> data)
        {
            int count = 0, samples = 0;
            if (data.IsEmpty) { throw new InvalidDataException("The AC-3 access unit is empty."); }
            for (int offset = 0; offset < data.Length;)
            {
                if (count == MaximumFramesPerAccessUnit) { throw new InvalidDataException("An E-AC-3 access unit exceeds six audio blocks."); }
                Ac3FrameHeader header = Ac3FrameHeader.Read(data[offset..]);
                if (header.IsEnhanced != enhanced) { throw new InvalidDataException("The AC-3 syncframe does not match the declared codec."); }
                if (header.FrameBytes > data.Length - offset || (!enhanced && header.FrameBytes != data.Length))
                {
                    throw new InvalidDataException("The AC-3 syncframe boundaries do not match the access unit.");
                }
                Ac3FrameDecoder.ValidateFrame(data.Slice(offset, header.FrameBytes), header);
                if (enhanced && header.StreamType == 1) { throw new NotSupportedException("Dependent E-AC-3 substream composition is not supported."); }
                Ac3FrameHeader? expected = configuration ?? (count == 0 ? null : frames[0]);
                if (expected is not null && (header.SampleRate != expected.SampleRate || header.ChannelMode != expected.ChannelMode ||
                    header.HasLfe != expected.HasLfe || header.SubstreamId != expected.SubstreamId))
                {
                    throw new NotSupportedException("AC-3 sample rate, channel configuration or independent substream changes require a new decoder.");
                }
                samples += header.SamplesPerFrame;
                if (samples > MaximumSamplesPerAccessUnit) { throw new InvalidDataException("An E-AC-3 access unit exceeds six audio blocks."); }
                frames[count++] = header; offset += header.FrameBytes;
            }
            return (count, samples);
        }
        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested) { Fault(); cancellationToken.ThrowIfCancellationRequested(); }
            if (pending is not null) { AudioFrame frame = pending; pending = null; return CodecOutput<AudioFrame>.Available(frame); }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }
        public void CompleteInput()
        {
            EnsureUsable();
            if (prerollPackets != 0) { Fault(); throw new InvalidDataException("AC-3 input ended before its requested preroll was complete."); }
            completed = true;
        }
        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount); Reset(); prerollPackets = packetCount;
        }
        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose(); pending = null; workspace?.Reset();
            Array.Clear(frames);
            configuration = null; format = null; prerollPackets = 0; completed = false; faulted = false;
        }
        private void Fault() { Reset(); faulted = true; }
        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The AC-3 decoder has failed and requires reset or disposal."); }
        }
        public void Dispose()
        {
            if (disposed) { return; }
            pending?.Dispose(); pending = null; workspace?.Dispose(); workspace = null; disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
