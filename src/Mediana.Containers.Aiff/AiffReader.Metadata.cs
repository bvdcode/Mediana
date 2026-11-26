// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;

namespace Mediana.Containers.Aiff
{
    public partial class AiffReader
    {
        private readonly List<MediaTag> tags = [];
        private long packetCount;

        private async ValueTask ReadNameAsync(uint size, CancellationToken token)
        {
            string title = string.Empty;
            if (size > 0)
            {
                CountMetadata(size);
                using WritableMediaBuffer text = allocator.Allocate(checked((int)size));
                await ReadAsync(text.Memory, token).ConfigureAwait(false);
                title = Encoding.Latin1.GetString(text.Span.TrimEnd((byte)0));
            }
            tags.Add(new("aiff", "NAME", title, sourcePath: "FORM/NAME"));
            await ReadPaddingAsync(size, token).ConfigureAwait(false);
        }
    }
}
