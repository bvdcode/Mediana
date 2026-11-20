// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Formats.Riff
{
    public static class RiffChunkReader
    {
        /// <summary>Enumerates a bounded RIFF/LIST body. Padding is present for odd payload lengths;
        /// formats such as WebP additionally require its value to be zero.</summary>
        public static IEnumerable<RiffChunk> Read(ReadOnlyMemory<byte> data, bool requireZeroPadding = false,
            CancellationToken cancellationToken = default)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (data.Length - offset < 8) { throw new InvalidDataException("Truncated RIFF chunk header."); }
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(offset + 4)..]);
                if ((ulong)size + (size & 1) > (ulong)(data.Length - offset - 8)) { throw new InvalidDataException("Truncated RIFF chunk payload."); }
                if (requireZeroPadding && (size & 1) != 0 && data.Span[offset + 8 + (int)size] != 0)
                {
                    throw new InvalidDataException("Invalid nonzero RIFF chunk padding.");
                }
                string type = Encoding.ASCII.GetString(data.Span.Slice(offset, 4));
                yield return new(type, data.Slice(offset + 8, (int)size));
                offset += checked(8 + (int)size + (int)(size & 1));
            }
        }
    }
}
