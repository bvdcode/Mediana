// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaBlockGroupReader
    {
        private static ReadOnlySpan<uint> Fields => [0xA1, 0x9B, 0x75A2, 0xA4, 0x75A1, 0xFA];

        public static async ValueTask<MatroskaBlockGroup> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token = default)
        {
            reader.ValidateMaster(master, 0xA0);
            EbmlElement? block = null;
            ulong? duration = null;
            long? padding = null;
            EbmlElement? codecState = null;
            EbmlElement? additions = null;
            ulong? priority = null;
            int references = 0;
            bool unknownReference = false;
            MatroskaFieldSet seen = new();
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                offset = child.EndOffset!.Value;
                if (child.Id == 0xFB)
                {
                    long reference = await reader.ReadSignedAsync(child, 0, token).ConfigureAwait(false);
                    references++;
                    unknownReference |= reference == 0;
                    if (unknownReference && references != 1)
                    {
                        throw new InvalidDataException("A zero Matroska ReferenceBlock must be the only reference.");
                    }
                    continue;
                }
                if (!seen.TryMark(child.Id, Fields))
                {
                    continue;
                }
                switch (child.Id)
                {
                    case 0xA1: block = child; break;
                    case 0x9B: duration = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x75A2: padding = await reader.ReadSignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0xA4: codecState = child; break;
                    case 0x75A1: additions = child; break;
                    case 0xFA: priority = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                }
            }
            if (block is not EbmlElement required)
            {
                throw new InvalidDataException("A Matroska BlockGroup requires exactly one Block.");
            }
            return new(required, references != 0, new(duration, padding, codecState, additions, priority));
        }
    }
}
