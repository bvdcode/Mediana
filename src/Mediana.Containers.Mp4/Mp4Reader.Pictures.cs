// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader : IContainerPictureReader
    {
        private IReadOnlyList<ContainerPictureInfo> pictureInfo = [];

        IReadOnlyList<ContainerPictureInfo> IContainerPictureReader.Pictures => pictureInfo;

        ValueTask<int> IContainerPictureReader.ReadPicturePrefixAsync(int pictureIndex, Memory<byte> destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer picture = ReadCoverArt(pictureIndex);
            int length = Math.Min(picture.Memory.Length, destination.Length);
            picture.Memory[..length].CopyTo(destination);
            return ValueTask.FromResult(length);
        }

        ValueTask<MediaBuffer> IContainerPictureReader.ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadCoverArt(pictureIndex));
        }

        private void InitializePictureInfo()
        {
            pictureInfo = Array.AsReadOnly(CoverArt.Select(info => new ContainerPictureInfo(info.ByteLength,
                PictureDataKind.Embedded, info.ContentType)).ToArray());
        }
    }
}
