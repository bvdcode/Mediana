using Mediana.Core.Memory;

namespace Mediana.Containers.Flac
{
    internal record FlacPicture(FlacPictureInfo Info, MediaBuffer Block, int DataOffset);
}
