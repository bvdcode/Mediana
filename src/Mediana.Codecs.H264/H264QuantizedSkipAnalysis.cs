// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264QuantizedSkipAnalysis
    {
        public static bool TryAnalyze(H264CabacMacroblockState state, H264SourceMacroblock source,
            H264Picture reference, int address, int x, int y, int motionX, int motionY, int quantizer,
            int searchRange, Span<byte> prediction, H264MotionReference? interpolation)
        {
            if (!IsSkipped(state, state.Left(address)) && !IsSkipped(state, state.Top(address)))
            {
                return false;
            }
            int limit = searchRange * 4;
            if (motionX < -limit || motionX > limit || motionY < -limit || motionY > limit)
            {
                return false;
            }
            if (interpolation is null)
            {
                H264PredictionBlock.Read(reference, 0, x, y, 16, 16, motionX, motionY, prediction[..256]);
            }
            else
            {
                H264LumaNeighborhood block = new(interpolation, x + (motionX >> 2), y + (motionY >> 2));
                block.Evaluate(motionX & 3, motionY & 3, source.Luma, prediction[..256], int.MaxValue);
            }
            if (HasLumaResidual(source.Luma, prediction[..256], quantizer))
            {
                return false;
            }
            H264PredictionBlock.Read(reference, 1, x, y, 16, 16, motionX, motionY, prediction.Slice(256, 64));
            H264PredictionBlock.Read(reference, 2, x, y, 16, 16, motionX, motionY, prediction.Slice(320, 64));
            Span<int> chroma = stackalloc int[H264ChromaEncoder.CoefficientCount];
            return H264ChromaEncoder.Analyze(source.Chroma, quantizer, prediction[256..], chroma) == 0;
        }

        private static bool HasLumaResidual(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int quantizer)
        {
            Span<int> levels = stackalloc int[32];
            for (int index = 0; index < 16; index += 2)
            {
                (int x, int y) = H264Picture.LumaBlockPosition(index);
                int offset = y * 16 + x;
                H264ForwardTransform.QuantizePair(source[offset..], 16, prediction[offset..], 16,
                    quantizer, levels[..16], levels[16..]);
                if (levels.ContainsAnyExcept(0))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSkipped(H264CabacMacroblockState state, int address) =>
            address >= 0 && state.Types[address] == H264CabacMacroblockState.Skipped;
    }
}
