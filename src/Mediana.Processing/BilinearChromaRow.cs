using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly ref struct BilinearChromaRow
    {
        private readonly ReadOnlySpan<byte> top;
        private readonly ReadOnlySpan<byte> bottom;
        private readonly double horizontalOffset;
        private readonly double verticalFraction;

        public BilinearChromaRow(VideoFrame frame, int plane, int lumaY, ChromaLocation location)
        {
            (double x, double y) = Position(location);
            horizontalOffset = x;
            int lastRow = frame.Planes[plane].Height - 1;
            double position = Math.Clamp((lumaY - y) / 2, 0, lastRow);
            int row = (int)position;
            verticalFraction = position - row;
            top = frame.GetRow(plane, row);
            bottom = frame.GetRow(plane, Math.Min(row + 1, lastRow));
        }

        public double Sample(int lumaX)
        {
            double position = Math.Clamp((lumaX - horizontalOffset) / 2, 0, top.Length - 1);
            int left = (int)position;
            int right = Math.Min(left + 1, top.Length - 1);
            double fraction = position - left;
            double upper = top[left] + (top[right] - top[left]) * fraction;
            double lower = bottom[left] + (bottom[right] - bottom[left]) * fraction;
            return upper + (lower - upper) * verticalFraction;
        }

        // Rec. ITU-T H.264, Figure E-1, positions in luma sample coordinates.
        private static (double X, double Y) Position(ChromaLocation location) => location switch
        {
            ChromaLocation.Left => (0, 0.5),
            ChromaLocation.Center => (0.5, 0.5),
            ChromaLocation.TopLeft => (0, 0),
            ChromaLocation.Top => (0.5, 0),
            ChromaLocation.BottomLeft => (0, 1),
            ChromaLocation.Bottom => (0.5, 1),
            ChromaLocation.Unspecified => throw new NotSupportedException("YCbCr conversion requires an explicit chroma location."),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };
    }
}
