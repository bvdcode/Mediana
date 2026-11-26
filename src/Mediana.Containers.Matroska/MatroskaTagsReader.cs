// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaTagsReader
    {
        private const uint Tag = 0x7373;
        private const uint Targets = 0x63C0;
        private const uint SimpleTag = 0x67C8;
        private const string TagPath = "Segment/Tags/Tag/SimpleTag";

        internal static async ValueTask ReadSegmentTagsAsync(MatroskaMetadataReader reader, EbmlElement master,
            List<MediaTag> tags, CancellationToken token)
        {
            for (long offset = master.DataOffset; await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child;)
            {
                offset = child.EndOffset!.Value;
                if (child.Id == Tag)
                {
                    await ReadTagAsync(reader, child, tags, token).ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask ReadTagAsync(MatroskaMetadataReader reader, EbmlElement tag,
            List<MediaTag> tags, CancellationToken token)
        {
            List<EbmlElement> strings = [];
            bool scoped = false;
            for (long offset = tag.DataOffset; await reader.ReadChildAsync(tag, offset, token).ConfigureAwait(false) is EbmlElement child;)
            {
                offset = child.EndOffset!.Value;
                if (child.Id == Targets)
                {
                    scoped |= await HasObjectTargetAsync(reader, child, token).ConfigureAwait(false);
                }
                else if (child.Id == SimpleTag)
                {
                    strings.Add(child);
                }
            }
            if (!scoped)
            {
                foreach (EbmlElement entry in strings)
                {
                    if (await ReadTextTagAsync(reader, entry, token).ConfigureAwait(false) is MediaTag text)
                    {
                        tags.Add(text);
                    }
                }
            }
        }

        private static async ValueTask<bool> HasObjectTargetAsync(MatroskaMetadataReader reader,
            EbmlElement targets, CancellationToken token)
        {
            bool scoped = false;
            for (long offset = targets.DataOffset;
                await reader.ReadChildAsync(targets, offset, token).ConfigureAwait(false) is EbmlElement child;)
            {
                offset = child.EndOffset!.Value;
                scoped |= child.Id is 0x63C5 or 0x63C9 or 0x63C4 or 0x63C6;
            }
            return scoped;
        }

        private static async ValueTask<MediaTag?> ReadTextTagAsync(MatroskaMetadataReader reader,
            EbmlElement entry, CancellationToken token)
        {
            string? name = null, value = null, language = null, bcp47 = null;
            for (long offset = entry.DataOffset; await reader.ReadChildAsync(entry, offset, token).ConfigureAwait(false) is EbmlElement child;)
            {
                offset = child.EndOffset!.Value;
                switch (child.Id)
                {
                    case 0x45A3: name = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x4487: value = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x447A: language = await reader.ReadTextAsync(child, false, token).ConfigureAwait(false); break;
                    case 0x447B: bcp47 = await reader.ReadTextAsync(child, false, token).ConfigureAwait(false); break;
                }
            }
            string? selectedLanguage = string.IsNullOrWhiteSpace(bcp47) ? language : bcp47;
            return string.IsNullOrEmpty(name) || value is null ? null :
                new("matroska", name, value, sourcePath: TagPath, language: string.IsNullOrWhiteSpace(selectedLanguage) ? null : selectedLanguage);
        }
    }
}
