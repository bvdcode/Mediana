// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaAttachmentsReader
    {
        private static ReadOnlySpan<uint> Fields => [0x467E, 0x466E, 0x4660, 0x465C, 0x46AE];

        public static async ValueTask<IReadOnlyList<MatroskaAttachmentInfo>> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, int maximumCount, MatroskaMetadataByteBudget byteBudget, bool verifyChecksums,
            CancellationToken token)
        {
            reader.ValidateMaster(master, 0x1941A469);
            List<MatroskaAttachmentInfo> attachments = [];
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token, verifyChecksums).ConfigureAwait(false) is EbmlElement child)
            {
                if (child.Id == 0x61A7)
                {
                    byteBudget.Reserve(child.DataOffset - offset);
                    if (attachments.Count == maximumCount)
                    {
                        throw new NotSupportedException("Matroska attachments exceed the configured file count.");
                    }
                    attachments.Add(await ReadEntryAsync(reader, child, byteBudget, verifyChecksums, token).ConfigureAwait(false));
                }
                else
                {
                    byteBudget.Reserve(child.EndOffset!.Value - offset);
                }
                offset = child.EndOffset!.Value;
            }
            if (attachments.Count == 0)
            {
                throw new InvalidDataException("Matroska Attachments must contain at least one AttachedFile.");
            }
            return attachments.AsReadOnly();
        }

        private static async ValueTask<MatroskaAttachmentInfo> ReadEntryAsync(MatroskaMetadataReader reader,
            EbmlElement master, MatroskaMetadataByteBudget byteBudget, bool verifyChecksums, CancellationToken token)
        {
            ulong uid = 0;
            string? name = null;
            string? mediaType = null;
            string? description = null;
            EbmlElement? data = null;
            MatroskaFieldSet seen = new();
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token, verifyChecksums).ConfigureAwait(false) is EbmlElement child)
            {
                byteBudget.Reserve((child.Id == 0x465C ? child.DataOffset : child.EndOffset!.Value) - offset);
                offset = child.EndOffset!.Value;
                if (!seen.TryMark(child.Id, Fields))
                {
                    continue;
                }
                switch (child.Id)
                {
                    case 0x467E: description = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x466E: name = await reader.ReadTextAsync(child, true, token).ConfigureAwait(false); break;
                    case 0x4660: mediaType = await reader.ReadTextAsync(child, false, token).ConfigureAwait(false); break;
                    case 0x465C: data = child; break;
                    case 0x46AE: uid = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                }
            }
            if (uid == 0 || name is null || mediaType is null || data is null)
            {
                throw new InvalidDataException("A Matroska attachment requires a nonzero FileUID, FileName, FileMediaType and FileData.");
            }
            return new(uid, name, mediaType, description, data.Value);
        }
    }
}
