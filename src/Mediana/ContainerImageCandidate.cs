// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;

namespace Mediana
{
    internal readonly record struct ContainerImageCandidate(ContainerImageSource Source, int Index, long ByteLength, PictureDataKind DataKind)
    {
        internal ValueTask<int> ReadPrefixAsync(ContainerSession session, Memory<byte> destination, CancellationToken token) => Source switch
        {
            ContainerImageSource.Picture => session.ReadPicturePrefixAsync(Index, destination, token),
            ContainerImageSource.Attachment => session.ReadAttachmentPrefixAsync(Index, destination, token),
            _ => throw new ArgumentOutOfRangeException(nameof(Source))
        };

        internal ValueTask<MediaBuffer> ReadAsync(ContainerSession session, CancellationToken token) => Source switch
        {
            ContainerImageSource.Picture => session.ReadPictureAsync(Index, token),
            ContainerImageSource.Attachment => session.ReadAttachmentAsync(Index, token),
            _ => throw new ArgumentOutOfRangeException(nameof(Source))
        };

        internal static IEnumerable<ContainerImageCandidate> Enumerate(ContainerSession session, IReadOnlyList<ContainerImageSource> sources)
        {
            foreach (ContainerImageSource source in sources)
            {
                switch (source)
                {
                    case ContainerImageSource.Picture:
                        if (session.Pictures is IReadOnlyList<ContainerPictureInfo> pictures)
                        {
                            for (int i = 0; i < pictures.Count; i++)
                            {
                                yield return new(source, i, pictures[i].ByteLength, pictures[i].DataKind);
                            }
                        }
                        break;
                    case ContainerImageSource.Attachment:
                        if (session.Attachments is IReadOnlyList<ContainerAttachmentInfo> attachments)
                        {
                            for (int i = 0; i < attachments.Count; i++)
                            {
                                yield return new(source, i, attachments[i].ByteLength, PictureDataKind.Embedded);
                            }
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(sources));
                }
            }
        }
    }
}
