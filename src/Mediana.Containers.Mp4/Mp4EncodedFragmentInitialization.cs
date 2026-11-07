// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4EncodedFragmentInitialization
    {
        public static MediaBuffer Create(IReadOnlyList<Mp4TrackDefinition> tracks, MediaBufferAllocator allocator, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Mp4BoxWriter measure = new(default);
            Build(measure, tracks);
            using WritableMediaBuffer storage = allocator.Allocate(measure.Position);
            Build(new(storage.Memory), tracks);
            token.ThrowIfCancellationRequested();
            return storage.Publish();
        }

        private static void Build(Mp4BoxWriter writer, IReadOnlyList<Mp4TrackDefinition> tracks)
        {
            TimeBase movieClock = new(1, 1000);
            foreach (Mp4TrackDefinition track in tracks)
            {
                if (track.PresentationEnd is MediaTimestamp end)
                { movieClock = TimeBase.Common(movieClock, end.ReduceClock().TimeBase); }
            }
            writer.Box("ftyp", "iso6\0\0\0\0iso6isommp41"u8);
            int moov = writer.Begin("moov");
            int mvhd = writer.Full("mvhd");
            writer.Zeros(8);
            writer.UInt32((uint)movieClock.Denominator);
            writer.UInt32(0);
            writer.UInt32(1 << 16);
            writer.UInt16(1 << 8);
            writer.Zeros(10);
            Matrix(writer, VideoDisplayMatrix.Identity);
            writer.Zeros(24);
            writer.UInt32(tracks.Max(track => track.TrackId) + 1);
            writer.End(mvhd);
            foreach (Mp4TrackDefinition track in tracks) { WriteTrack(writer, track, movieClock); }
            int mvex = writer.Begin("mvex");
            foreach (Mp4TrackDefinition track in tracks)
            {
                int trex = writer.Full("trex");
                writer.UInt32(track.TrackId);
                writer.UInt32(1);
                writer.Zeros(12);
                writer.End(trex);
            }
            writer.End(mvex);
            writer.End(moov);
        }

        private static void WriteTrack(Mp4BoxWriter writer, Mp4TrackDefinition track, TimeBase movieClock)
        {
            int trak = writer.Begin("trak"), tkhd = writer.Full("tkhd", 7);
            writer.Zeros(8);
            writer.UInt32(track.TrackId);
            writer.Zeros(20);
            writer.UInt16(track.Stream.Kind switch
            {
                MediaKind.Audio => 256,
                MediaKind.Video => 0,
                MediaKind.Data => throw new NotSupportedException("Data tracks require their own header declaration."),
                _ => throw new ArgumentOutOfRangeException(nameof(track))
            });
            writer.Zeros(2);
            Matrix(writer, track.Stream.DisplayMatrix ?? VideoDisplayMatrix.Identity);
            track.WritePresentationSize(writer);
            writer.End(tkhd);
            if (track.TimestampOffset != 0 || track.PresentationEnd is not null)
            {
                int edts = writer.Begin("edts"), elst = writer.Full("elst", 0x01000000);
                writer.UInt32(1);
                writer.UInt64(track.PresentationEnd is MediaTimestamp end ? checked((ulong)end.Rescale(movieClock, TimestampRounding.TowardZero).Value) : 0);
                writer.UInt64((ulong)track.TimestampOffset);
                writer.UInt32(1 << 16);
                writer.End(elst);
                writer.End(edts);
            }
            int mdia = writer.Begin("mdia"), mdhd = writer.Full("mdhd");
            writer.Zeros(8);
            writer.UInt32((uint)track.TimeBase.Denominator);
            writer.UInt32(0);
            writer.UInt16(0x55c4);
            writer.UInt16(0);
            writer.End(mdhd);
            int hdlr = writer.Full("hdlr");
            writer.UInt32(0);
            switch (track.Stream.Kind)
            {
                case MediaKind.Video:
                    writer.Bytes("vide"u8);
                    writer.Zeros(12);
                    writer.Bytes("VideoHandler\0"u8);
                    break;
                case MediaKind.Audio:
                    writer.Bytes("soun"u8);
                    writer.Zeros(12);
                    writer.Bytes("SoundHandler\0"u8);
                    break;
                case MediaKind.Data:
                    throw new NotSupportedException("Data tracks require their own handler declaration.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(track));
            }
            writer.End(hdlr);
            WriteMediaInfo(writer, track);
            writer.End(mdia);
            writer.End(trak);
        }

        private static void WriteMediaInfo(Mp4BoxWriter writer, Mp4TrackDefinition track)
        {
            int minf = writer.Begin("minf");
            track.WriteMediaHeader(writer);
            int dinf = writer.Begin("dinf"), dref = writer.Full("dref");
            writer.UInt32(1);
            writer.End(writer.Full("url ", 1));
            writer.End(dref);
            writer.End(dinf);
            int stbl = writer.Begin("stbl"), stsd = writer.Full("stsd");
            writer.UInt32(1);
            track.WriteSampleEntry(writer);
            writer.End(stsd);
            foreach (string type in new[] { "stts", "stsc", "stsz", "stco" })
            {
                int box = writer.Full(type);
                writer.UInt32(0);
                if (type == "stsz") { writer.UInt32(0); }
                writer.End(box);
            }
            writer.End(stbl);
            writer.End(minf);
        }

        private static void Matrix(Mp4BoxWriter writer, VideoDisplayMatrix matrix)
        {
            Fixed(writer, matrix.M11, 16);
            Fixed(writer, matrix.M12, 16);
            Fixed(writer, matrix.M13, 30);
            Fixed(writer, matrix.M21, 16);
            Fixed(writer, matrix.M22, 16);
            Fixed(writer, matrix.M23, 30);
            Fixed(writer, matrix.M31, 16);
            Fixed(writer, matrix.M32, 16);
            Fixed(writer, matrix.M33, 30);
        }

        private static void Fixed(Mp4BoxWriter writer, double value, int bits)
        {
            double scaled = value * (1L << bits);
            if (scaled < int.MinValue || scaled > int.MaxValue || scaled != Math.Truncate(scaled))
            {
                throw new NotSupportedException("The display transform cannot be represented exactly by an MP4 matrix.");
            }
            writer.UInt32(unchecked((uint)(int)scaled));
        }
    }
}
