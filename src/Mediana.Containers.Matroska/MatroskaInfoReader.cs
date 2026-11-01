// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaInfoReader
    {
        private static ReadOnlySpan<uint> Fields => [0x2AD7B1, 0x4489, 0x4461, 0x7BA9, 0x4D80, 0x5741, 0x73A4, 0x3CB923, 0x3EB923];

        public static async ValueTask<MatroskaSegmentInfo> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token = default)
        {
            reader.ValidateMaster(master, 0x1549A966);
            ulong scale = 1000000;
            double? duration = null;
            long? date = null;
            string? title = null;
            string? muxingApp = null;
            string? writingApp = null;
            Guid? uid = null;
            Guid? previous = null;
            Guid? next = null;
            MatroskaFieldSet seen = new();
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                offset = child.EndOffset!.Value;
                if (!seen.TryMark(child.Id, Fields))
                {
                    continue;
                }
                switch (child.Id)
                {
                    case 0x2AD7B1: scale = await reader.ReadUnsignedAsync(child, scale, token).ConfigureAwait(false); break;
                    case 0x4489: duration = await reader.ReadPositiveFloatAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x4461: date = await reader.ReadDateAsync(child, token).ConfigureAwait(false); break;
                    case 0x7BA9: title = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x4D80: muxingApp = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x5741: writingApp = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x73A4: uid = await reader.ReadUuidAsync(child, token).ConfigureAwait(false); break;
                    case 0x3CB923: previous = await reader.ReadUuidAsync(child, token).ConfigureAwait(false); break;
                    case 0x3EB923: next = await reader.ReadUuidAsync(child, token).ConfigureAwait(false); break;
                }
            }
            if (scale == 0 || muxingApp is null || writingApp is null)
            {
                throw new InvalidDataException("Matroska Info requires a nonzero TimestampScale, MuxingApp and WritingApp.");
            }
            if ((uid is not null && (previous == uid || next == uid)) || ((previous is not null || next is not null) && uid is null))
            {
                throw new InvalidDataException("A linked Matroska Segment requires distinct local and neighbor UUIDs.");
            }
            return new(scale, duration, date, title, muxingApp, writingApp, uid, previous, next);
        }
    }
}
