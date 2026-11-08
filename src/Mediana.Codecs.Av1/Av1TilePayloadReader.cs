// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1TilePayloadReader
    {
        private readonly Av1TileInfo info;
        private readonly bool frameObu;
        private Av1ObuReader obus;
        private Av1TileGroupReader group;
        private bool readingGroup;
        private bool completed;
        private int nextTile;

        internal Av1TilePayloadReader(ReadOnlySpan<byte> encoded, Av1TileInfo info, bool frameObu)
        {
            this.info = info;
            this.frameObu = frameObu;
            obus = frameObu ? default : new(encoded);
            group = frameObu ? new(encoded, info, 0, true) : default;
            readingGroup = frameObu;
        }

        internal bool TryRead(out int index, out ReadOnlySpan<byte> encoded, CancellationToken cancellationToken)
        {
            while (!completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (readingGroup && group.TryRead(out index, out encoded))
                {
                    nextTile = group.NextTile;
                    return true;
                }
                readingGroup = false;
                if (!frameObu)
                {
                    while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        switch (type)
                        {
                            case 4:
                                group = new(payload, info, nextTile, false);
                                readingGroup = true;
                                break;
                            case 0:
                            case 5:
                            case 9:
                            case 10:
                            case 11:
                            case 12:
                            case 13:
                            case 14:
                            case 15:
                                continue;
                            case 1:
                            case 2:
                            case 3:
                            case 6:
                            case 7:
                            case 8:
                                throw new InvalidDataException("An AV1 tile sequence contains an unexpected OBU.");
                            default:
                                throw new InvalidDataException("The AV1 OBU type is invalid.");
                        }
                        break;
                    }
                    if (readingGroup) { continue; }
                }
                if (nextTile != info.Count)
                {
                    throw new InvalidDataException("The AV1 frame is missing one or more tile groups.");
                }
                completed = true;
            }
            index = 0;
            encoded = default;
            return false;
        }
    }
}
