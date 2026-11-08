// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264CabacEncodingSyntax(H264CabacEncoder encoder, H264CabacMacroblockState state,
        H264Picture target, H264SliceKind kind, int quantizer)
        : H264CabacSyntax<H264CabacOutputWriter>(new(encoder), state, target, kind, quantizer)
    {
        public H264CabacRateSample RateSample => writer.Encoder.RateSample;
        public H264CabacEncoderCheckpoint CaptureEngine() => writer.Encoder.CreateCheckpoint();
        public void RestoreEngine(in H264CabacEncoderCheckpoint checkpoint) => writer.Encoder.Restore(checkpoint);
    }
}
