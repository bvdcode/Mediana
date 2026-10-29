namespace Mediana.Codecs.H264
{
    internal static class H264PredictionBlock
    {
        public static void Read(H264Picture reference, int plane, int x, int y, int width, int height,
            int motionX, int motionY, Span<byte> samples)
        {
            switch (plane)
            {
                case 0:
                    H264MotionCompensation.Luma(reference, x, y, motionX, motionY, width, height, samples);
                    break;
                case 1:
                case 2:
                    H264MotionCompensation.Chroma(reference, plane, x / 2, y / 2, motionX, motionY, width / 2, height / 2, samples);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(plane));
            }
        }

        public static void Write(int plane, int x, int y, int width, int height, ReadOnlySpan<byte> samples, Span<byte> prediction)
        {
            int destination = plane switch
            {
                0 => 0,
                1 => 256,
                2 => 320,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            int scale = plane == 0 ? 1 : 2;
            int stride = 16 / scale;
            int blockWidth = width / scale;
            int blockHeight = height / scale;
            destination += (y % 16) / scale * stride + (x % 16) / scale;
            for (int row = 0; row < blockHeight; row++)
            {
                samples.Slice(row * blockWidth, blockWidth).CopyTo(prediction.Slice(destination + row * stride, blockWidth));
            }
        }
    }
}
