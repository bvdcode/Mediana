using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4DisplayMatrixParser
    {
        public static VideoDisplayMatrix Read(ReadOnlySpan<byte> data, int offset)
        {
            ReadOnlySpan<byte> matrix = Mp4Binary.Range(data, offset, 36);
            return new VideoDisplayMatrix(
                Mp4Binary.Int32(matrix, 0) / 65536.0,
                Mp4Binary.Int32(matrix, 4) / 65536.0,
                Mp4Binary.Int32(matrix, 8) / 1073741824.0,
                Mp4Binary.Int32(matrix, 12) / 65536.0,
                Mp4Binary.Int32(matrix, 16) / 65536.0,
                Mp4Binary.Int32(matrix, 20) / 1073741824.0,
                Mp4Binary.Int32(matrix, 24) / 65536.0,
                Mp4Binary.Int32(matrix, 28) / 65536.0,
                Mp4Binary.Int32(matrix, 32) / 1073741824.0);
        }
    }
}
