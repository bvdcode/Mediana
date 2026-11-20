// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Mp3
{
    /// <summary>Writes one Layer III stream with Xing counts and ID3v2.4 iTunSMPB presentation trimming.
    /// Borrows destination and packets. Requires a seekable output to finalize bounded metadata without buffering audio.</summary>
    public class Mp3Writer : IContainerWriter
    {
        private const int TagBytes = 256;
        private readonly Stream destination;
        private readonly EncodedStreamInfo stream;
        private readonly Mp3FrameHeader header;
        private readonly Mp3FrameHeader metadataHeader;
        private readonly WritableMediaBuffer scratch;
        private readonly long origin;
        private long frames;
        private long audioBytes;
        private long? timestampOrigin;
        private int priming;
        private int padding;
        private bool endTrimSeen;
        private bool started;
        private bool completed;
        private bool disposed;
        private bool faulted;
        private int busy;

        private Mp3Writer(Stream destination, EncodedStreamInfo stream, Mp3FrameHeader header, MediaBufferAllocator allocator)
        {
            this.destination = destination;
            this.stream = stream;
            this.header = header;
            origin = destination.Position;
            int metadataRate = header.Version switch
            {
                MpegAudioVersion.Mpeg1 => 128000,
                MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25 => 64000,
                _ => throw new InvalidOperationException("The MP3 version is invalid.")
            };
            metadataHeader = Mp3FrameHeader.Create(header.SampleRate, header.ChannelCount, metadataRate);
            scratch = allocator.Allocate(Math.Max(TagBytes, metadataHeader.FrameBytes));
        }

        public static Mp3Writer Create(Stream destination, IReadOnlyList<EncodedStreamInfo> streams, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(streams);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!destination.CanWrite || !destination.CanSeek) { throw new NotSupportedException("MP3 file output requires a writable seekable destination."); }
            if (streams.Count != 1 || streams[0].Kind != MediaKind.Audio || streams[0].CodecId != Mp3FrameHeader.CodecId)
            { throw new NotSupportedException("MP3 output requires exactly one Layer III audio stream."); }
            EncodedStreamInfo stream = streams[0];
            string? reason = StreamFormats.Mp3.GetUnsupportedReason(stream.Format);
            if (reason is not null) { throw new NotSupportedException(reason); }
            Mp3FrameHeader header = Mp3FrameHeader.Read(stream.Configuration.Span);
            return new(destination, stream, header, allocator);
        }

        public async ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                if (packet.StreamIndex != stream.StreamIndex || endTrimSeen) { throw new InvalidDataException("MP3 packets must belong to the configured stream and precede end trimming."); }
                if (packet.Data.Length < 4) { throw new InvalidDataException("The MP3 packet is truncated."); }
                Mp3FrameHeader current = Mp3FrameHeader.Read(packet.Data.Span[..4]);
                if (current.SampleRate != header.SampleRate || current.Version != header.Version || current.ChannelCount != header.ChannelCount || current.FrameBytes != packet.Data.Length)
                { throw new InvalidDataException("MP3 packets require complete frames with a consistent decoded format."); }
                ValidateTiming(packet);
                if (packet.TryGetSideData(AudioPacketTrim.Kind, out MediaPacketSideDataView data))
                {
                    AudioPacketTrim trim = AudioPacketTrim.Read(data);
                    if (frames != 0 && trim.StartSamples != 0) { throw new InvalidDataException("MP3 start trimming must occur on the first packet."); }
                    if (frames == 0) { priming = trim.StartSamples; }
                    padding = trim.EndSamples;
                    endTrimSeen = padding != 0;
                }
                if (!started)
                {
                    scratch.Memory.Span.Clear();
                    await destination.WriteAsync(scratch.Memory[..TagBytes], cancellationToken).ConfigureAwait(false);
                    await destination.WriteAsync(scratch.Memory[..metadataHeader.FrameBytes], cancellationToken).ConfigureAwait(false);
                    started = true;
                }
                await destination.WriteAsync(packet.Data, cancellationToken).ConfigureAwait(false);
                frames = checked(frames + 1);
                audioBytes = checked(audioBytes + packet.Data.Length);
                success = true;
            }
            finally { faulted |= !success; Volatile.Write(ref busy, 0); }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (frames == 0) { throw new InvalidDataException("An MP3 file requires at least one audio frame."); }
                long samples = checked(frames * header.SamplesPerFrame - priming - padding);
                if (samples <= 0) { throw new InvalidDataException("MP3 presentation trimming removes all audio."); }
                long end = destination.Position;
                destination.Position = origin;
                WriteTag(samples);
                await destination.WriteAsync(scratch.Memory[..TagBytes], cancellationToken).ConfigureAwait(false);
                WriteXing();
                await destination.WriteAsync(scratch.Memory[..metadataHeader.FrameBytes], cancellationToken).ConfigureAwait(false);
                destination.Position = end;
                completed = true;
                success = true;
            }
            finally { faulted |= !success; Volatile.Write(ref busy, 0); }
        }

        private void WriteXing()
        {
            Span<byte> data = scratch.Memory.Span[..metadataHeader.FrameBytes];
            data.Clear();
            metadataHeader.Write(data[..4]);
            int offset = 4 + metadataHeader.SideInfoBytes;
            "Xing"u8.CopyTo(data[offset..]);
            uint flags = (frames <= uint.MaxValue ? 1u : 0u) | (audioBytes + metadataHeader.FrameBytes <= uint.MaxValue ? 2u : 0u);
            BinaryPrimitives.WriteUInt32BigEndian(data[(offset + 4)..], flags);
            int position = offset + 8;
            if ((flags & 1) != 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data[position..], (uint)frames);
                position += 4;
            }
            if ((flags & 2) != 0)
            { BinaryPrimitives.WriteUInt32BigEndian(data[position..], (uint)(audioBytes + metadataHeader.FrameBytes)); }
        }

        private void ValidateTiming(MediaPacket packet)
        {
            TimeBase clock = new(1, header.SampleRate);
            if (packet.Duration is MediaDuration duration && duration != new MediaDuration(header.SamplesPerFrame, clock))
            { throw new InvalidDataException("An MP3 packet duration must describe its complete coded frame."); }
            if (packet.PresentationTime is MediaTimestamp pts && packet.DecodeTime is MediaTimestamp dts && pts != dts)
            { throw new InvalidDataException("MP3 does not reorder audio presentation."); }
            if ((packet.PresentationTime ?? packet.DecodeTime) is MediaTimestamp timestamp)
            {
                long samples = timestamp.Rescale(clock, TimestampRounding.TowardZero).Value;
                if (new MediaTimestamp(samples, clock) != timestamp)
                { throw new InvalidDataException("MP3 timestamps must lie on the exact sample grid."); }
                long candidate = checked(samples - frames * header.SamplesPerFrame);
                if (timestampOrigin is long origin && origin != candidate)
                { throw new InvalidDataException("MP3 file output cannot preserve a timestamp gap or overlap."); }
                timestampOrigin ??= candidate;
            }
        }

        private void WriteTag(long samples)
        {
            Span<byte> data = scratch.Memory.Span[..TagBytes];
            data.Clear();
            "ID3"u8.CopyTo(data);
            data[3] = 4;
            WriteSynchsafe(data[6..10], TagBytes - 10);
            "TXXX"u8.CopyTo(data[10..]);
            data[20] = 3;
            string value = $"iTunSMPB\0 00000000 {priming:X8} {padding:X8} {samples:X16} 00000000 00000000 00000000 00000000 00000000 00000000 00000000 00000000";
            int length = Encoding.UTF8.GetBytes(value, data[21..]);
            WriteSynchsafe(data[14..18], length + 1);
        }

        private static void WriteSynchsafe(Span<byte> data, int value)
        {
            for (int index = 3; index >= 0; index--) { data[index] = (byte)(value & 127); value >>= 7; }
            if (value != 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
        }

        private void RequireActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed || faulted) { throw new InvalidOperationException("The MP3 writer is no longer active."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent MP3 writer operations are not supported."); }
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
