// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4RecoveryGroupParser
    {
        public static IReadOnlyList<Mp4SampleRecoveryGroup> Parse(Mp4BoxCollection boxes, int sampleCount,
            Mp4ReadOptions options, CancellationToken token)
        {
            List<Mp4SampleRecoveryGroup> groups = [];
            foreach (string type in new[] { "roll", "prol" })
            {
                token.ThrowIfCancellationRequested();
                Mp4Box? description = Find(boxes, "sgpd", type);
                Mp4Box? mapping = Find(boxes, "sbgp", type);
                if (description is null)
                {
                    if (mapping is not null)
                    {
                        throw new InvalidDataException("An MP4 recovery mapping has no group description.");
                    }
                    continue;
                }
                (short[] entries, uint defaultIndex) = ReadDescriptions(description.Value.Payload.Span, options, token);
                List<(int End, short? Distance)> runs = [];
                if (mapping is not null)
                {
                    ReadMapping(mapping.Value.Payload.Span, entries, sampleCount, options, runs, token);
                }
                Mp4RecoveryKind kind = type switch
                {
                    "roll" => Mp4RecoveryKind.Roll,
                    "prol" => Mp4RecoveryKind.PreRoll,
                    _ => throw new InvalidDataException("Unknown MP4 recovery grouping type.")
                };
                groups.Add(new(kind, sampleCount, Distance(entries, defaultIndex), runs));
            }
            return groups.AsReadOnly();
        }

        private static Mp4Box? Find(Mp4BoxCollection boxes, string boxType, string groupType)
        {
            Mp4Box? found = null;
            foreach (Mp4Box box in boxes.Boxes)
            {
                if (box.Type == boxType && Mp4Binary.FourCc(box.Payload.Span, 4) == groupType)
                {
                    if (found is not null)
                    {
                        throw new InvalidDataException("An MP4 recovery grouping box is duplicated.");
                    }
                    found = box;
                }
            }
            return found;
        }

        private static (short[], uint) ReadDescriptions(ReadOnlySpan<byte> data, Mp4ReadOptions options,
            CancellationToken token)
        {
            byte version = Mp4Binary.Range(data, 0, 1)[0];
            if (version > 2)
            {
                throw new NotSupportedException("The MP4 recovery description version is unsupported.");
            }
            Mp4Binary.FullBox(data, version);
            int offset = 8;
            uint length = 2;
            if (version >= 1)
            {
                length = Mp4Binary.UInt32(data, offset);
                offset += 4;
            }
            uint defaultIndex = 0;
            if (version >= 2)
            {
                defaultIndex = Mp4Binary.UInt32(data, offset);
                offset += 4;
            }
            uint count = Mp4Binary.UInt32(data, offset);
            offset += 4;
            if (count > options.MaxSamplesPerTrack)
            {
                throw new NotSupportedException("The MP4 recovery description count exceeds the configured limit.");
            }
            int entryBytes = length == 0 ? 6 : 2;
            if ((length != 0 && length != 2) || (long)offset + count * (long)entryBytes != data.Length)
            {
                throw new InvalidDataException("The MP4 recovery descriptions have invalid lengths.");
            }
            short[] entries = new short[(int)count];
            for (int index = 0; index < entries.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (length == 0)
                {
                    if (Mp4Binary.UInt32(data, offset) != 2)
                    {
                        throw new InvalidDataException("An MP4 recovery entry must contain a signed 16-bit distance.");
                    }
                    offset += 4;
                }
                entries[index] = BinaryPrimitives.ReadInt16BigEndian(Mp4Binary.Range(data, offset, 2));
                if (entries[index] == 0)
                {
                    throw new InvalidDataException("An MP4 recovery distance must be nonzero.");
                }
                offset += 2;
            }
            Distance(entries, defaultIndex);
            return (entries, defaultIndex);
        }

        private static void ReadMapping(ReadOnlySpan<byte> data, short[] entries, int sampleCount,
            Mp4ReadOptions options, List<(int End, short? Distance)> runs, CancellationToken token)
        {
            byte version = Mp4Binary.Range(data, 0, 1)[0];
            if (version > 1)
            {
                throw new NotSupportedException("The MP4 recovery mapping version is unsupported.");
            }
            Mp4Binary.FullBox(data, version);
            int offset = 8;
            if (version == 1)
            {
                if (Mp4Binary.UInt32(data, offset) != 0)
                {
                    throw new NotSupportedException("Parameterized MP4 recovery groups are unsupported.");
                }
                offset += 4;
            }
            uint count = Mp4Binary.UInt32(data, offset);
            offset += 4;
            if (count > options.MaxSamplesPerTrack)
            {
                throw new NotSupportedException("The MP4 recovery run count exceeds the configured limit.");
            }
            if ((long)offset + count * 8L != data.Length)
            {
                throw new InvalidDataException("The MP4 recovery run count differs from its box length.");
            }
            int end = 0;
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                uint samples = Mp4Binary.UInt32(data, offset);
                short? distance = Distance(entries, Mp4Binary.UInt32(data, offset + 4));
                if (samples > sampleCount - end)
                {
                    throw new InvalidDataException("MP4 recovery runs exceed the track sample count.");
                }
                end += (int)samples;
                if (samples != 0)
                {
                    runs.Add((end, distance));
                }
                offset += 8;
            }
        }

        private static short? Distance(short[] entries, uint index)
        {
            if (index > entries.Length)
            {
                throw new InvalidDataException("An MP4 recovery group references a missing description.");
            }
            return index == 0 ? null : entries[index - 1];
        }
    }
}
