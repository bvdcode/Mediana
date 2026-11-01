// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    /// <summary>Native AttachedFile declarations. FileData remains in the borrowed source until requested.</summary>
    public class MatroskaAttachmentInfo : ContainerAttachmentInfo
    {
        internal MatroskaAttachmentInfo(ulong uid, string fileName, string mediaType, string? description, EbmlElement data)
            : base(data.DataSize!.Value, fileName, mediaType, description)
        {
            Uid = uid;
            Data = data;
        }

        public ulong Uid { get; }
        internal EbmlElement Data { get; }
    }
}
