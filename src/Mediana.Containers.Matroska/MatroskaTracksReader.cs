// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaTracksReader
    {
        private static ReadOnlySpan<uint> Fields => [0xD7, 0x73C5, 0x83, 0x86, 0x63A2,
            0xB9, 0x88, 0x55AA, 0x9C, 0x23E383, 0x23314F, 0x56AA, 0x56BB,
            0x536E, 0x22B59C, 0x22B59D, 0xE0, 0xE1, 0x6D80, 0xE2];

        public static async ValueTask<IReadOnlyList<MatroskaTrack>> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token = default)
        {
            reader.ValidateMaster(master, 0x1654AE6B);
            List<MatroskaTrack> tracks = [];
            HashSet<ulong> numbers = [];
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                offset = child.EndOffset!.Value;
                if (child.Id != 0xAE)
                {
                    continue;
                }
                reader.ReserveTrack();
                MatroskaTrack track = await ReadEntryAsync(reader, child, token).ConfigureAwait(false);
                if (!numbers.Add(track.Number))
                {
                    throw new InvalidDataException("Matroska track numbers must be distinct.");
                }
                tracks.Add(track);
            }
            if (tracks.Count == 0)
            {
                throw new InvalidDataException("Matroska Tracks must contain at least one TrackEntry.");
            }
            return tracks.AsReadOnly();
        }

        private static async ValueTask<MatroskaTrack> ReadEntryAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token)
        {
            ulong number = 0;
            ulong uid = 0;
            ulong type = 0;
            string? codec = null;
            EbmlElement? configuration = null;
            bool enabled = true;
            bool defaultTrack = true;
            bool forced = false;
            bool lacing = true;
            double scale = 1;
            ulong? duration = null;
            ulong delay = 0;
            ulong preroll = 0;
            string? name = null;
            string language = "eng";
            string? bcp47 = null;
            MatroskaVideoSettings? video = null;
            MatroskaAudioSettings? audio = null;
            EbmlElement? encodings = null;
            EbmlElement? operation = null;
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
                    case 0xD7: number = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x73C5: uid = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x83: type = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x86: codec = await reader.ReadTextAsync(child, false, token).ConfigureAwait(false); break;
                    case 0x63A2: configuration = child; break;
                    case 0xB9: enabled = await reader.ReadFlagAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x88: defaultTrack = await reader.ReadFlagAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x55AA: forced = await reader.ReadFlagAsync(child, false, token).ConfigureAwait(false); break;
                    case 0x9C: lacing = await reader.ReadFlagAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x23E383: duration = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x23314F: scale = await reader.ReadPositiveFloatAsync(child, 1, token).ConfigureAwait(false); break;
                    case 0x56AA: delay = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x56BB: preroll = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x536E: name = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x22B59C: language = await reader.ReadTextAsync(child, false, token, "eng").ConfigureAwait(false); break;
                    case 0x22B59D: bcp47 = await reader.ReadTextAsync(child, false, token).ConfigureAwait(false); break;
                    case 0xE0: video = await MatroskaVideoReader.ReadAsync(reader, child, token).ConfigureAwait(false); break;
                    case 0xE1: audio = await MatroskaAudioReader.ReadAsync(reader, child, token).ConfigureAwait(false); break;
                    case 0x6D80: encodings = child; break;
                    case 0xE2: operation = child; break;
                }
            }
            if (number == 0 || uid == 0 || type == 0 || string.IsNullOrEmpty(codec) || duration == 0)
            {
                throw new InvalidDataException("A Matroska track requires positive identifiers, CodecID and any declared DefaultDuration.");
            }
            return new(number, uid, type, codec, new(enabled, defaultTrack, forced, lacing), new(scale, duration, delay, preroll),
                new(name, language, bcp47), configuration, video, audio, encodings, operation);
        }
    }
}
