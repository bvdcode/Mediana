// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;

namespace Mediana.Images.Metadata
{
    internal static class Cr3JpegTrackPreviews
    {
        internal static void Read(ReadOnlyMemory<byte> file, ReadOnlyMemory<byte> movie, IReadOnlyList<Mp4DataRange> media,
            RawJpegPreviews previews, CancellationToken token)
        {
            Mp4ReadOptions options = new() { MaxBoxes = int.MaxValue, MaxSamplesPerTrack = int.MaxValue };
            Mp4ParseBudget budget = new(options, token);
            Mp4BoxCollection boxes = new(movie, budget);
            foreach (Mp4Box track in boxes.Boxes.Where(box => box.Type == "trak"))
            {
                token.ThrowIfCancellationRequested();
                Mp4BoxCollection trackBoxes = new(track.Payload, budget);
                Mp4BoxCollection mediaBoxes = new(trackBoxes.Required("mdia").Payload, budget);
                Mp4BoxCollection mediaInfo = new(mediaBoxes.Required("minf").Payload, budget);
                Mp4BoxCollection samples = new(mediaInfo.Required("stbl").Payload, budget);
                ReadOnlyMemory<byte> descriptions = samples.Required("stsd").Payload;
                if (descriptions.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(descriptions.Span) != 0)
                { throw new InvalidDataException("Invalid Canon sample description header."); }
                uint count = BinaryPrimitives.ReadUInt32BigEndian(descriptions.Span[4..]);
                Mp4BoxCollection entries = new(descriptions[8..], budget);
                if (count != entries.Boxes.Count) { throw new InvalidDataException("Canon sample description count differs."); }
                bool jpeg = false;
                foreach (Mp4Box entry in entries.Boxes)
                {
                    if (entry.Type != "CRAW") { continue; }
                    if (entry.Payload.Length < 82) { throw new InvalidDataException("Truncated Canon visual sample description."); }
                    Mp4BoxCollection coding = new(entry.Payload[82..], budget);
                    jpeg |= coding.Optional("JPEG") is not null;
                }
                if (!jpeg) { continue; }
                if (count != 1) { throw new NotSupportedException("Changing Canon JPEG sample descriptions are not supported."); }
                Mp4SampleTable table = new(samples, options, media, false, token);
                IMp4SampleCursor cursor = table.CreateCursor();
                while (cursor.HasSample)
                {
                    token.ThrowIfCancellationRequested();
                    Mp4SampleLocation sample = cursor.Current;
                    previews.Add(file.Slice(checked((int)sample.Offset), sample.Size));
                    cursor.Advance(sample);
                }
            }
        }
    }
}
