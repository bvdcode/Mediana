// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;

namespace Mediana.Containers.Wave
{
    public partial class WaveReader
    {
        private readonly List<MediaTag> tags = [];
        private long packetCount;

        private async ValueTask ReadInfoAsync(long remainingSize, CancellationToken token)
        {
            while (remainingSize != 0)
            {
                if (remainingSize < 8)
                {
                    throw new InvalidDataException("A RIFF INFO entry is truncated.");
                }
                CountMetadata(8);
                await ReadAsync(scratch.Memory[..8], token).ConfigureAwait(false);
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[4..]);
                long padded = (long)size + (size & 1);
                remainingSize -= 8;
                if (padded > remainingSize)
                {
                    throw new InvalidDataException("A RIFF INFO entry exceeds its list.");
                }
                string? name = id switch
                {
                    0x4D414E49 => "INAM",
                    0x54524149 => "IART",
                    _ => null
                };
                if (name is null)
                {
                    await SkipMetadataAsync(padded, token).ConfigureAwait(false);
                }
                else
                {
                    string value = string.Empty;
                    if (size != 0)
                    {
                        CountMetadata(size);
                        using WritableMediaBuffer text = allocator.Allocate(checked((int)size));
                        await ReadAsync(text.Memory, token).ConfigureAwait(false);
                        value = Encoding.Latin1.GetString(text.Span.TrimEnd((byte)0));
                    }
                    tags.Add(new("riff-info", name, value, sourcePath: "RIFF/LIST/INFO"));
                    await SkipMetadataAsync(size & 1, token).ConfigureAwait(false);
                }
                remainingSize -= padded;
            }
        }
    }
}
