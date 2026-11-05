// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs.Jbig2
{
    internal partial class DocumentDecoder
    {
        internal ImageInfo ReadInfo(ReadOnlyMemory<byte> encoded)
        {
            SortedDictionary<uint, PageHeader> pages = new();
            Read(encoded, false, segment =>
            {
                if (segment.Type != 48)
                {
                    return;
                }
                PageHeader header = PageHeader.Read(segment, new BitReader(segment.Data), _budget);
                if (pages.Count >= _budget.Options.MaximumPages || !pages.TryAdd(segment.Page, header))
                {
                    throw new InvalidDataException("Duplicate JBIG2 page or MaximumPages exceeded.");
                }
            });
            if (pages.Count == 0)
            {
                throw new InvalidDataException("JBIG2 contains no pages.");
            }
            PageHeader first = pages.First().Value;
            return new ImageInfo("jbig2", first.Width, first.Height, ImageDimensionUnit.Pixels, pages.Count,
                metadata: new("image", [new("media", "codec", "jbig2"), new("media", "bit_depth", 1)],
                    pages.Select(page => new MediaMetadata("page/" + page.Key,
                        [new("media", "dimensions", page.Value.Width, page.Value.Height)]))));
        }
    }
}
