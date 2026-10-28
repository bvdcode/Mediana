namespace Mediana.Core.Video
{
    /// <summary>Layout of a byte-addressable plane; subsampling is relative to the coded image.</summary>
    public record VideoPlaneFormat
    {
        public VideoPlaneFormat(int horizontalSubsampling, int verticalSubsampling, int bytesPerElement)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizontalSubsampling);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verticalSubsampling);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerElement);
            HorizontalSubsampling = horizontalSubsampling;
            VerticalSubsampling = verticalSubsampling;
            BytesPerElement = bytesPerElement;
        }

        public int HorizontalSubsampling { get; }
        public int VerticalSubsampling { get; }
        public int BytesPerElement { get; }
    }
}
