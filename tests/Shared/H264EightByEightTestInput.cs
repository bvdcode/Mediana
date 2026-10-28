using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264EightByEightTestInput
    {
        public static async Task VerifyPredictionVectorsAsync(string root)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "intra8-modes.yuv"));
            const int frameBytes = 32 * 32 * 3 / 2;
            if (reference.Length != 9 * frameBytes)
            {
                throw new InvalidDataException("The intra 8x8 prediction reference has an unexpected size.");
            }
            byte[] prediction = new byte[64];
            byte[] top = new byte[16];
            byte[] left = new byte[8];
            for (int mode = 0; mode < 9; mode++)
            {
                ReadOnlySpan<byte> frame = reference.AsSpan(mode * frameBytes, frameBytes);
                for (int block = 0; block < 4; block++)
                {
                    int x = 16 + block % 2 * 8;
                    int y = 16 + block / 2 * 8;
                    for (int index = 0; index < 16; index++)
                    {
                        bool topRightUnavailable = x + index >= 32;
                        top[index] = frame[(y - 1) * 32 + (topRightUnavailable ? x + 7 : x + index)];
                    }
                    for (int index = 0; index < 8; index++)
                    {
                        left[index] = frame[(y + index) * 32 + x - 1];
                    }
                    H264IntraPrediction.Luma8x8(mode, top, left, frame[(y - 1) * 32 + x - 1], prediction);
                    for (int row = 0; row < 8; row++)
                    {
                        if (!prediction.AsSpan(row * 8, 8).SequenceEqual(frame.Slice((y + row) * 32 + x, 8)))
                        {
                            throw new InvalidDataException($"Intra 8x8 prediction differs in mode {mode}, block {block}, row {row}.");
                        }
                    }
                }
            }
        }
    }
}
