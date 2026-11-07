// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4VideoFragmentInitialization
    {
        public static MediaBuffer Create(Mp4VideoTrackDefinition track, MediaBufferAllocator allocator, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Mp4BoxWriter measure = new(default);
            Build(measure, track);
            using WritableMediaBuffer storage = allocator.Allocate(measure.Position);
            Build(new(storage.Memory), track);
            token.ThrowIfCancellationRequested();
            return storage.Publish();
        }

        private static void Build(Mp4BoxWriter writer, Mp4VideoTrackDefinition track)
        {
            writer.Box("ftyp", "iso6\0\0\0\0iso6isommp41"u8);
            int moov = writer.Begin("moov");
            int mvhd = writer.Full("mvhd");
            writer.Zeros(8);
            writer.UInt32(1000);
            writer.UInt32(0);
            writer.UInt32(1 << 16);
            writer.UInt16(1 << 8);
            writer.Zeros(10);
            Matrix(writer, VideoDisplayMatrix.Identity);
            writer.Zeros(24);
            writer.UInt32(track.TrackId + 1);
            writer.End(mvhd);
            int trak = writer.Begin("trak"), tkhd = writer.Full("tkhd", 7);
            writer.Zeros(8);
            writer.UInt32(track.TrackId);
            writer.Zeros(24);
            Matrix(writer, track.Stream.DisplayMatrix!.Value);
            double width = track.Format.Width;
            if (track.SampleAspectRatio is SampleAspectRatio aspect) { width = width * aspect.Numerator / aspect.Denominator; }
            writer.UInt32(checked((uint)Math.Round(width * 65536, MidpointRounding.ToEven)));
            writer.UInt32(checked((uint)track.Format.Height << 16));
            writer.End(tkhd);
            int mdia = writer.Begin("mdia"), mdhd = writer.Full("mdhd");
            writer.Zeros(8);
            writer.UInt32((uint)track.TimeBase.Denominator);
            writer.UInt32(0);
            writer.UInt16(0x55c4);
            writer.UInt16(0);
            writer.End(mdhd);
            int hdlr = writer.Full("hdlr");
            writer.UInt32(0);
            writer.Bytes("vide"u8);
            writer.Zeros(12);
            writer.Bytes("VideoHandler\0"u8);
            writer.End(hdlr);
            WriteMediaInfo(writer, track);
            writer.End(mdia);
            writer.End(trak);
            int mvex = writer.Begin("mvex"), trex = writer.Full("trex");
            writer.UInt32(track.TrackId);
            writer.UInt32(1);
            writer.Zeros(12);
            writer.End(trex);
            writer.End(mvex);
            writer.End(moov);
        }

        private static void WriteMediaInfo(Mp4BoxWriter writer, Mp4VideoTrackDefinition track)
        {
            int minf = writer.Begin("minf"), vmhd = writer.Full("vmhd", 1);
            writer.Zeros(8);
            writer.End(vmhd);
            int dinf = writer.Begin("dinf"), dref = writer.Full("dref");
            writer.UInt32(1);
            writer.End(writer.Full("url ", 1));
            writer.End(dref);
            writer.End(dinf);
            int stbl = writer.Begin("stbl"), stsd = writer.Full("stsd");
            writer.UInt32(1);
            int sample = writer.Begin(track.SampleEntry);
            writer.Zeros(6);
            writer.UInt16(1);
            writer.Zeros(16);
            writer.UInt16((ushort)track.Format.Width);
            writer.UInt16((ushort)track.Format.Height);
            writer.UInt32(72 << 16);
            writer.UInt32(72 << 16);
            writer.UInt32(0);
            writer.UInt16(1);
            writer.Zeros(32);
            writer.UInt16(24);
            writer.UInt16(ushort.MaxValue);
            writer.Box(track.ConfigurationBox, track.Stream.Configuration.Span);
            if (track.SampleAspectRatio is SampleAspectRatio aspect)
            {
                int pasp = writer.Begin("pasp");
                writer.UInt32((uint)aspect.Numerator);
                writer.UInt32((uint)aspect.Denominator);
                writer.End(pasp);
            }
            writer.End(sample);
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
