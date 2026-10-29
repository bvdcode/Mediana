// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Mp3
{
    internal class Mp3Decoder : IMediaCodec<MediaPacket, AudioFrame>
    {
        private readonly int streamIndex;
        private readonly Mp3FrameHeader configuration;
        private readonly MediaBufferAllocator allocator;
        private readonly int maxFrameBytes;
        private readonly AudioFormat format;
        private Mp3DecoderWorkspace? workspace;
        private AudioFrame? pending;
        private readonly Mp3FrameInfo frameInfo = new();
        private bool completed;
        private bool disposed;
        private bool faulted;

        public Mp3Decoder(int streamIndex, Mp3FrameHeader configuration, MediaBufferAllocator allocator, int maxFrameBytes)
        {
            this.streamIndex = streamIndex;
            this.configuration = configuration;
            this.allocator = allocator;
            this.maxFrameBytes = maxFrameBytes;
            string[] channels = configuration.ChannelCount switch
            {
                1 => ["front-center"],
                2 => ["front-left", "front-right"],
                _ => throw new NotSupportedException("The MP3 channel layout is not supported.")
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
                    throw new InvalidOperationException("MP3 input is complete.");
                }
                if (pending is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty)
                {
                    throw new InvalidDataException("The MP3 packet is empty or belongs to a different stream.");
                }
                if (input.Data.Length > maxFrameBytes)
                {
                    throw new NotSupportedException("The MP3 packet exceeds the configured frame limit.");
                }
                Mp3FrameHeader header = frameInfo.Read(input.Data.Span);
                if (header.Version != configuration.Version || header.SampleRate != configuration.SampleRate
                    || header.ChannelCount != configuration.ChannelCount || header.Emphasis != 0)
                {
                    throw new NotSupportedException("MP3 format changes or de-emphasis are not supported.");
                }
                workspace ??= new Mp3DecoderWorkspace(allocator, configuration, maxFrameBytes);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(header.SamplesPerFrame));
                Span<float> output = MemoryMarshal.Cast<byte, float>(writable.Memory.Span);
                Mp3FrameDecoder.Decode(input.Data.Span, header, frameInfo, workspace, output, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer buffer = writable.Publish();
                pending = new AudioFrame(format, header.SamplesPerFrame, buffer, input.PresentationTime);
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
                throw new InvalidOperationException("The MP3 decoder has failed and requires reset or disposal.");
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
