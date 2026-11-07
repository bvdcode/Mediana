// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4FragmentInitialization
    {
        public static MediaBuffer Create(ReadOnlyMemory<byte> metadata, IReadOnlyList<Mp4TrackInfo> tracks,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            Mp4BoxWriter measure = new(default);
            Build(measure, metadata, tracks, token);
            using WritableMediaBuffer storage = allocator.Allocate(measure.Position);
            Mp4BoxWriter writer = new(storage.Memory);
            Build(writer, metadata, tracks, token);
            return storage.Publish();
        }

        private static void Build(Mp4BoxWriter writer, ReadOnlyMemory<byte> metadata,
            IReadOnlyList<Mp4TrackInfo> tracks, CancellationToken token)
        {
            Mp4ParseBudget budget = new(new() { MaxBoxes = int.MaxValue }, token);
            Mp4BoxCollection movie = new(metadata, budget);
            writer.Box("ftyp", "iso6\0\0\0\0iso6isommp41"u8);
            int moov = writer.Begin("moov");
            Mp4Box header = movie.Required("mvhd");
            bool wide = header.Payload.Span[0] == 1;
            writer.HeaderWithoutDuration(header, wide ? 24 : 16, wide ? 8 : 4);
            Dictionary<uint, Mp4BoxCollection> selected = [];
            foreach (Mp4Box box in movie.Boxes.Where(box => box.Type == "trak"))
            {
                Mp4BoxCollection source = new(box.Payload, budget);
                ReadOnlySpan<byte> trackHeader = source.Required("tkhd").Payload.Span;
                uint id = Mp4Binary.UInt32(trackHeader, trackHeader[0] == 1 ? 20 : 12);
                if (tracks.Any(track => track.TrackId == id)) { selected.Add(id, source); }
            }
            foreach (Mp4TrackInfo track in tracks)
            {
                token.ThrowIfCancellationRequested();
                WriteTrack(writer, selected[track.TrackId], budget);
            }
            int mvex = writer.Begin("mvex");
            foreach (Mp4TrackInfo track in tracks)
            {
                int trex = writer.Full("trex");
                writer.UInt32(track.TrackId);
                writer.UInt32(1);
                writer.UInt32(0);
                writer.UInt32(0);
                writer.UInt32(0);
                writer.End(trex);
            }
            writer.End(mvex);
            writer.End(moov);
        }

        private static void WriteTrack(Mp4BoxWriter writer, Mp4BoxCollection track, Mp4ParseBudget budget)
        {
            int trak = writer.Begin("trak");
            Mp4Box header = track.Required("tkhd");
            bool wide = header.Payload.Span[0] == 1;
            writer.HeaderWithoutDuration(header, wide ? 28 : 20, wide ? 8 : 4);
            if (track.Optional("edts") is Mp4Box edits) { writer.Box(edits.Type, edits.Payload.Span); }
            Mp4BoxCollection media = new(track.Required("mdia").Payload, budget);
            int mdia = writer.Begin("mdia");
            header = media.Required("mdhd");
            wide = header.Payload.Span[0] == 1;
            writer.HeaderWithoutDuration(header, wide ? 24 : 16, wide ? 8 : 4);
            Mp4Box handler = media.Required("hdlr");
            writer.Box(handler.Type, handler.Payload.Span);
            if (media.Optional("elng") is Mp4Box language) { writer.Box(language.Type, language.Payload.Span); }
            Mp4BoxCollection info = new(media.Required("minf").Payload, budget);
            int minf = writer.Begin("minf");
            foreach (Mp4Box box in info.Boxes.Where(box => box.Type is "vmhd" or "smhd" or "dinf"))
            {
                writer.Box(box.Type, box.Payload.Span);
            }
            Mp4BoxCollection samples = new(info.Required("stbl").Payload, budget);
            int stbl = writer.Begin("stbl");
            Mp4Box description = samples.Required("stsd");
            writer.Box(description.Type, description.Payload.Span);
            foreach (string type in new[] { "stts", "stsc", "stsz", "stco" })
            {
                int empty = writer.Full(type);
                writer.UInt32(0);
                if (type == "stsz") { writer.UInt32(0); }
                writer.End(empty);
            }
            writer.End(stbl);
            writer.End(minf);
            writer.End(mdia);
            writer.End(trak);
        }
    }
}
