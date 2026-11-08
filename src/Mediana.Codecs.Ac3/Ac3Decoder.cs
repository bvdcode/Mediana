// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3Decoder(int streamIndex, bool enhanced, MediaBufferAllocator allocator, bool applyDynamicRangeCompression,
        Ac3DecoderExpectation? expectation = null) : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private Ac3FrameDecoder? workspace;
        private Ac3FrameHeader? configuration;
        private AudioFormat? format;
        private AudioFrame? pending;
        private Ac3TransientProcessing? transientProcessing;
        private Eac3AccessUnit? enhancedAccessUnit;
        private Eac3ProgramLayout? enhancedProgram;
        private bool originalBeginning = true;
        private long prerollPackets;
        private bool disposed, completed, faulted;
        public int RequiredPrerollPackets => enhanced ? Ac3TransientProcessing.InitialPrerollPackets : 1;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input); EnsureUsable();
            bool success = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("AC-3 input is complete."); }
                if (pending is not null || transientProcessing is not null && transientProcessing.HasOutput) { success = true; return CodecInputStatus.OutputRequired; }
                if (input.StreamIndex != streamIndex) { throw new InvalidDataException("The AC-3 packet belongs to a different stream."); }
                if (enhanced)
                {
                    ProcessEnhanced(input, cancellationToken);
                    if (prerollPackets > 0) { prerollPackets--; }
                    success = true; return CodecInputStatus.Accepted;
                }
                Ac3FrameHeader header = ReadAc3Frame(input.Data.Span);
                int codedSamples = header.SamplesPerFrame;
                configuration ??= header;
                format ??= new(header.SampleRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, header.GetChannelOrder());
                workspace ??= new(allocator, applyDynamicRangeCompression);
                int count = AudioPacketTiming.GetPresentationSamples(input.Duration, header.SampleRate, codedSamples);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(codedSamples));
                Span<float> pcm = MemoryMarshal.Cast<byte, float>(writable.Span);
                workspace.Decode(input.Data.Span, header, pcm, cancellationToken);
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

        private Ac3FrameHeader ReadAc3Frame(ReadOnlySpan<byte> data)
        {
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            if (header.IsEnhanced) { throw new InvalidDataException("The syncframe does not match the declared AC-3 codec."); }
            expectation?.Validate(header);
            if (header.FrameBytes != data.Length)
            { throw new InvalidDataException("An AC-3 access unit must contain exactly one complete syncframe."); }
            Ac3FrameDecoder.ValidateFrame(data, header);
            if (configuration is not null && (header.SampleRate != configuration.SampleRate ||
                header.ChannelMode != configuration.ChannelMode || header.HasLfe != configuration.HasLfe))
            { throw new NotSupportedException("AC-3 sampling format changes require a new decoder."); }
            return header;
        }
        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested) { Fault(); cancellationToken.ThrowIfCancellationRequested(); }
            if (pending is not null) { AudioFrame frame = pending; pending = null; return CodecOutput<AudioFrame>.Available(frame); }
            if (transientProcessing is not null && transientProcessing.HasOutput)
            {
                bool success = false;
                try
                {
                    AudioFrame frame = transientProcessing.Receive(); success = true;
                    return CodecOutput<AudioFrame>.Available(frame);
                }
                finally { if (!success) { Fault(); } }
            }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }
        public void CompleteInput()
        {
            EnsureUsable();
            if (prerollPackets != 0) { Fault(); throw new InvalidDataException("AC-3 input ended before its requested preroll was complete."); }
            bool success = false;
            try { transientProcessing?.CompleteInput(); completed = true; success = true; }
            finally { if (!success) { Fault(); } }
        }
        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount); Reset(); prerollPackets = packetCount; originalBeginning = startsAtStreamBeginning;
        }
        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose(); pending = null; workspace?.Reset();
            transientProcessing?.Dispose(); transientProcessing = null; originalBeginning = true;
            enhancedProgram?.Dispose(); enhancedProgram = null; enhancedAccessUnit = null;
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
            pending?.Dispose(); pending = null; transientProcessing?.Dispose(); transientProcessing = null;
            workspace?.Dispose(); workspace = null; enhancedProgram?.Dispose(); enhancedProgram = null; enhancedAccessUnit = null; disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
