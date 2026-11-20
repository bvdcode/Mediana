// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    public partial class AviReader
    {
        private async ValueTask ReadMp3IndexAsync(AviTrack track, CancellationToken token)
        {
            AviPacketIndex chunks = track.Packets;
            if (chunks.Count == 0) { throw new InvalidDataException("An AVI MP3 stream contains no audio frames."); }
            AviPacketIndex normalized = new(allocator);
            bool transferred = false;
            try
            {
                long chunkIndex = 0;
                int offset = 0;
                long remaining = track.PayloadBytes;
                long samples = 0;
                Mp3FrameHeader? first = null;
                long start = 0;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (remaining < 4) { throw new InvalidDataException("The AVI MP3 frame header is truncated."); }
                    await ReadAudioBytesAsync(chunks, chunkIndex, offset, scratch.Memory.Slice(16, 8), token).ConfigureAwait(false);
                    Mp3FrameHeader header = Mp3FrameHeader.Read(scratch.Span.Slice(16, 4));
                    if (first is null)
                    {
                        ReadOnlyMemory<byte> wave = track.Stream.Configuration;
                        if (BinaryPrimitives.ReadUInt16LittleEndian(wave.Span[2..]) != header.ChannelCount ||
                            BinaryPrimitives.ReadUInt32LittleEndian(wave.Span[4..]) != header.SampleRate)
                        { throw new InvalidDataException("The AVI WAVE format disagrees with its MP3 frames."); }
                        TimeBase clock = new(1, header.SampleRate);
                        MediaTimestamp original = new(track.Start, track.Stream.TimeBase);
                        start = original.Rescale(clock, TimestampRounding.TowardZero).Value;
                        if (new MediaTimestamp(start, clock) != original)
                        { throw new NotSupportedException("The AVI audio start is not sample-exact."); }
                        first = header;
                    }
                    else if (header.SampleRate != first.SampleRate || header.Version != first.Version || header.ChannelCount != first.ChannelCount)
                    { throw new NotSupportedException("The AVI MP3 decoded format changes within the stream."); }
                    if (header.FrameBytes > remaining) { throw new InvalidDataException("The AVI MP3 frame extends beyond the audio chunks."); }
                    int sideOffset = 16 + Mp3FrameHeader.HeaderBytes + (header.HasCrc ? 2 : 0);
                    bool independent = header.Version switch
                    {
                        MpegAudioVersion.Mpeg1 => scratch.Span[sideOffset] == 0 && (scratch.Span[sideOffset + 1] & 128) == 0,
                        MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25 => scratch.Span[sideOffset] == 0,
                        _ => throw new InvalidOperationException("The MP3 version is invalid.")
                    };
                    AviPacketEntry chunk = chunks[chunkIndex];
                    normalized.Add(new(chunk.Offset + offset, checked(start + samples), header.SamplesPerFrame, header.FrameBytes,
                        chunk.ChunkId, independent, chunk.Offset, chunk.Length, SourceChunkIndex: chunkIndex));
                    samples = checked(samples + header.SamplesPerFrame);
                    remaining -= header.FrameBytes;
                    int advance = header.FrameBytes;
                    while (advance != 0)
                    {
                        int take = Math.Min(advance, chunks[chunkIndex].Length - offset);
                        advance -= take;
                        offset += take;
                        if (offset == chunks[chunkIndex].Length) { chunkIndex++; offset = 0; }
                    }
                }
                byte[] configuration = new byte[4];
                first!.Write(configuration);
                EncodedStreamInfo stream = new(track.Stream.StreamIndex, MediaKind.Audio, Mp3FrameHeader.CodecId,
                    StreamFormats.Mp3, new(1, first.SampleRate), configuration, codedDuration: new(samples, new(1, first.SampleRate)));
                track.SetAudioPackets(normalized, stream, start, checked(start + samples));
                transferred = true;
            }
            finally { if (!transferred) { normalized.Dispose(); } }
        }

        private async ValueTask ReadAudioBytesAsync(AviPacketIndex chunks, long chunkIndex, int offset,
            Memory<byte> destination, CancellationToken token)
        {
            int written = 0;
            while (written < destination.Length)
            {
                token.ThrowIfCancellationRequested();
                if (chunkIndex >= chunks.Count) { throw new InvalidDataException("The AVI audio access unit is truncated."); }
                AviPacketEntry entry = chunks[chunkIndex];
                AviChunk chunk = await ReadChunkAsync(entry.ChunkDataOffset - 8, sourceLength, token).ConfigureAwait(false);
                if (chunk.Id != entry.ChunkId || chunk.Length != entry.ChunkLength)
                { throw new InvalidDataException("The AVI audio index disagrees with its data chunk."); }
                int take = Math.Min(destination.Length - written, entry.Length - offset);
                await source.ReadExactlyAtAsync(entry.Offset + offset, destination.Slice(written, take), token).ConfigureAwait(false);
                written += take;
                chunkIndex++;
                offset = 0;
            }
        }
    }
}
