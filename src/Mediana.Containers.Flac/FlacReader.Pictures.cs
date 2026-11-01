// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Flac
{
    public partial class FlacReader : IContainerPictureReader
    {
        private IReadOnlyList<ContainerPictureInfo> pictureInfo = [];

        IReadOnlyList<ContainerPictureInfo> IContainerPictureReader.Pictures => pictureInfo;

        ValueTask<MediaBuffer> IContainerPictureReader.ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadPicture(pictureIndex));
        }

        private void InitializePictureInfo()
        {
            pictureInfo = Array.AsReadOnly(Pictures.Select(info => new ContainerPictureInfo(info.ByteLength,
                info.MediaType switch
                {
                    "-->" => PictureDataKind.ExternalReference,
                    _ => PictureDataKind.Embedded
                }, info.MediaType, info.Description, info.PictureType)).ToArray());
        }
    }
}
