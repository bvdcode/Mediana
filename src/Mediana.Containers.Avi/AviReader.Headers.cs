// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    public partial class AviReader
    {
        private async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            sourceLength = source.Length ?? throw new NotSupportedException("AVI indexing requires a known source length.");
            uint? declaredStreams = null;
            MediaDuration? declaredDuration = null;
            long position = 0;
            while (position < sourceLength)
            {
                AviChunk riff = await ReadChunkAsync(position, sourceLength, cancellationToken).ConfigureAwait(false);
                if (riff.Id != Riff || await ReadListTypeAsync(riff, cancellationToken).ConfigureAwait(false) != (position == 0 ? Avi : Avix))
                {
                    throw new InvalidDataException("AVI requires an initial AVI RIFF followed only by AVIX RIFF segments.");
                }
                AviChunk? movie = null;
                for (long offset = riff.Data + 4; offset < riff.End;)
                {
                    AviChunk chunk = await ReadChunkAsync(offset, riff.End, cancellationToken).ConfigureAwait(false);
                    if (chunk.Id == List)
                    {
                        uint type = await ReadListTypeAsync(chunk, cancellationToken).ConfigureAwait(false);
                        switch (type)
                        {
                            case Hdrl:
                                if (position != 0 || declaredStreams is not null) { throw new InvalidDataException("AVI has a duplicate or misplaced header list."); }
                                (declaredStreams, declaredDuration) = await ReadHeadersAsync(chunk, cancellationToken).ConfigureAwait(false);
                                break;
                            case Movi:
                                if (declaredStreams is null || movie is not null) { throw new InvalidDataException("AVI requires headers and one movie list per RIFF segment."); }
                                movie = chunk;
                                movies.Add(chunk);
                                break;
                        }
                    }
                    else if (chunk.Id == Idx1)
                    {
                        if (movie is null) { throw new InvalidDataException("An AVI index precedes its movie list."); }
                        oldIndexes.Add((chunk, movie.Value));
                    }
                    offset = chunk.Next;
                }
                if (movie is null) { throw new InvalidDataException("An AVI RIFF segment has no movie list."); }
                position = riff.Next;
            }
            if (declaredStreams != tracks.Count || tracks.Count == 0) { throw new InvalidDataException("The AVI stream count disagrees with its stream lists."); }
            bool indexed = false;
            foreach (AviTrack track in tracks)
            {
                if (track.Index is AviChunk index)
                {
                    indexed = true;
                    await ReadOpenDmlIndexAsync(track, index, cancellationToken).ConfigureAwait(false);
                }
            }
            foreach ((AviChunk index, AviChunk movie) in oldIndexes)
            {
                indexed = true;
                await ReadOldIndexAsync(index, movie, cancellationToken).ConfigureAwait(false);
            }
            if (!indexed)
            {
                foreach (AviChunk movie in movies)
                {
                    await ScanMovieAsync(movie.Data + 4, movie.End, 0, cancellationToken).ConfigureAwait(false);
                }
            }
            List<EncodedStreamInfo> streams = new(tracks.Count);
            List<ContainerStreamInfo> metadata = new(tracks.Count);
            foreach (AviTrack track in tracks)
            {
                TimeBase clock = track.Stream.TimeBase;
                streams.Add(track.Stream);
                FrameRate? rate = null;
                if (track.Stream.Kind == MediaKind.Video) { rate = new(clock.Denominator, clock.Numerator); }
                metadata.Add(new(track.Stream.StreamIndex, new(track.DeclaredLength, clock), packetIndex:
                    new(track.Packets.Count, track.PayloadBytes, new(track.Time - track.Start, clock)), averageFrameRate: rate));
            }
            Streams = streams.AsReadOnly();
            Info = new("avi", declaredDuration, streamInfo: metadata);
        }

        private async ValueTask<(uint Streams, MediaDuration? Duration)> ReadHeadersAsync(AviChunk header, CancellationToken cancellationToken)
        {
            uint? streams = null;
            MediaDuration? duration = null;
            for (long offset = header.Data + 4; offset < header.End;)
            {
                AviChunk chunk = await ReadChunkAsync(offset, header.End, cancellationToken).ConfigureAwait(false);
                if (chunk.Id == 0x68697661)
                {
                    if (streams is not null || chunk.Length < 56) { throw new InvalidDataException("The AVI main header is duplicated or truncated."); }
                    await source.ReadExactlyAtAsync(chunk.Data, scratch.Memory[..56], cancellationToken).ConfigureAwait(false);
                    uint microseconds = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span);
                    uint frames = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[16..]);
                    streams = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[24..]);
                    if (streams is 0 or > 100) { throw new NotSupportedException("AVI stream chunk names support up to 100 streams."); }
                    if (microseconds != 0) { duration = new(checked((long)microseconds * frames), new(1, 1_000_000)); }
                }
                else if (chunk.Id == List && await ReadListTypeAsync(chunk, cancellationToken).ConfigureAwait(false) == Strl)
                {
                    if (streams is null || tracks.Count >= streams) { throw new InvalidDataException("An AVI stream list exceeds the main header declaration."); }
                    await ReadTrackAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
                offset = chunk.Next;
            }
            return (streams ?? throw new InvalidDataException("The AVI main header is missing."), duration);
        }

        private async ValueTask ReadTrackAsync(AviChunk list, CancellationToken cancellationToken)
        {
            AviChunk? header = null;
            AviChunk? format = null;
            AviChunk? index = null;
            for (long offset = list.Data + 4; offset < list.End;)
            {
                AviChunk chunk = await ReadChunkAsync(offset, list.End, cancellationToken).ConfigureAwait(false);
                switch (chunk.Id)
                {
                    case 0x68727473:
                        if (header is not null || format is not null) { throw new InvalidDataException("The AVI stream header is duplicated or follows its format."); }
                        header = chunk;
                        break;
                    case 0x66727473:
                        if (header is null || format is not null) { throw new InvalidDataException("The AVI stream format is duplicated or precedes its header."); }
                        format = chunk;
                        break;
                    case 0x78646e69:
                        if (index is not null) { throw new InvalidDataException("An AVI stream has multiple OpenDML indexes."); }
                        index = chunk;
                        break;
                }
                offset = chunk.Next;
            }
            if (header is null || format is null || header.Value.Length < 56 || format.Value.Length > int.MaxValue || format.Value.Length == 0)
            {
                throw new InvalidDataException("An AVI stream requires a complete stream header and format.");
            }
            await source.ReadExactlyAtAsync(header.Value.Data, scratch.Memory[..56], cancellationToken).ConfigureAwait(false);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span);
            uint scale = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[20..]);
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[24..]);
            uint start = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[28..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[32..]);
            uint sampleSize = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[44..]);
            if (scale == 0 || rate == 0) { throw new InvalidDataException("An AVI stream requires a positive rational clock."); }
            uint a = scale;
            uint b = rate;
            while (b != 0) { (a, b) = (b, a % b); }
            if (scale / a > int.MaxValue || rate / a > int.MaxValue) { throw new NotSupportedException("The reduced AVI clock exceeds the supported rational terms."); }
            TimeBase clock = new((int)(scale / a), (int)(rate / a));
            using WritableMediaBuffer storage = allocator.Allocate((int)format.Value.Length);
            await source.ReadExactlyAtAsync(format.Value.Data, storage.Memory, cancellationToken).ConfigureAwait(false);
            EncodedStreamInfo stream = CreateStream(type, sampleSize, clock, length, storage.Span);
            AviTrack track = new(stream, sampleSize, start, length, new(allocator)) { Index = index };
            bool added = false;
            try { tracks.Add(track); added = true; }
            finally { if (!added) { track.Dispose(); } }
        }

        private EncodedStreamInfo CreateStream(uint type, uint sampleSize, TimeBase clock, uint length, ReadOnlySpan<byte> format)
        {
            MediaDuration duration = new(length, clock);
            int streamIndex = tracks.Count;
            switch (type)
            {
                case 0x73646976:
                    if (format.Length < 40 || BinaryPrimitives.ReadUInt32LittleEndian(format) < 40 || BinaryPrimitives.ReadUInt32LittleEndian(format) > format.Length)
                    {
                        throw new InvalidDataException("An AVI video stream requires a complete bitmap header.");
                    }
                    uint compression = BinaryPrimitives.ReadUInt32LittleEndian(format[16..]);
                    if (compression == 0x47504a4d)
                    {
                        return new(streamIndex, MediaKind.Video, "mjpeg", StreamFormats.Mjpeg, clock,
                            displayMatrix: VideoDisplayMatrix.Identity, codedDuration: duration);
                    }
                    return new(streamIndex, MediaKind.Video, NativeId(compression), NativeFormat, clock, format, codedDuration: duration);
                case 0x73647561:
                    if (format.Length < 16) { throw new InvalidDataException("An AVI audio stream requires a complete WAVE format."); }
                    PcmConfiguration? pcm = PcmConfiguration.ReadWaveFormat(format, out _);
                    if (pcm is not null)
                    {
                        if (clock != new TimeBase(1, pcm.Format.SampleRate) || sampleSize != pcm.BlockAlignment)
                        {
                            throw new NotSupportedException("AVI PCM requires its declared sample clock and block alignment to agree.");
                        }
                        return new(streamIndex, MediaKind.Audio, PcmConfiguration.CodecId, StreamFormats.WavePcm, clock, format, codedDuration: duration);
                    }
                    return new(streamIndex, MediaKind.Audio, $"avi.wave-{BinaryPrimitives.ReadUInt16LittleEndian(format):x4}", NativeFormat,
                        clock, format, codedDuration: duration);
                default:
                    return new(streamIndex, MediaKind.Data, NativeId(type), NativeFormat, clock, format, codedDuration: duration);
            }
        }

        private static string NativeId(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            foreach (byte item in bytes)
            {
                if (item < 32 || item > 126) { return $"avi.fourcc-{value:x8}"; }
            }
            return "avi." + Encoding.ASCII.GetString(bytes);
        }

        private static EncodedStreamFormat NativeFormat { get; } = new("avi.stream-format", "avi.chunk", EncodedPacketBoundary.ArbitraryBytes);
    }
}
