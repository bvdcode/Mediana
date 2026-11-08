// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264PictureEncoder
    {
        private const int MaximumAttempts = 8;

        public static H264EncodedPicture Encode(VideoFrame input, H264SequenceParameterSet sequence, H264Picture? reference,
            bool keyFrame, int frameNumber, int idrId, MediaBufferAllocator allocator, H264EncoderOptions options,
            H264FrameRateBudget budget, ref H264Picture? workspace, CancellationToken token, H264MotionReference? interpolation = null)
        {
            int maximumBytes = Math.Min(options.MaximumPacketBytes, budget.MaximumBytes);
            if (maximumBytes < 8)
            {
                throw new NotSupportedException("The H.264 rate budget cannot fit an encoded picture.");
            }
            int quantizer = budget.Quantizer;
            token.ThrowIfCancellationRequested();
            bool reused = workspace is not null;
            H264Picture picture = workspace ?? new(sequence, allocator, keyFrame ? H264SliceKind.I : H264SliceKind.P);
            workspace = null;
            bool accepted = false;
            try
            {
                if (reused)
                {
                    picture.ResetForEncoding();
                }
                for (int attempt = 0; attempt < MaximumAttempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (attempt != 0)
                    {
                        picture.ResetForEncoding();
                    }
                    MediaBuffer? data = null;
                    try
                    {
                        int bytes;
                        data = keyFrame
                            ? H264IntraEncoder.Encode(input, picture, quantizer, options.Quantizer, idrId, allocator, maximumBytes,
                                options.RateControl is not null, out bytes, token)
                            : H264InterEncoder.Encode(input, picture, reference!, frameNumber, quantizer, allocator, options, maximumBytes,
                                options.RateControl is not null, out bytes, token, interpolation);
                        if (data is not null)
                        {
                            H264EncodedPicture result = new(picture, data, quantizer);
                            accepted = true;
                            return result;
                        }
                        if (options.RateControl is null)
                        {
                            throw new NotSupportedException("The encoded H.264 packet exceeds the configured byte limit at the fixed quantizer.");
                        }
                        if (quantizer == 51)
                        {
                            throw new NotSupportedException("The H.264 rate budget cannot fit the picture at QP51; increase its bitrate or buffer duration.");
                        }
                        quantizer = attempt == MaximumAttempts - 2 ? 51 : H264RateController.NextQuantizer(quantizer, bytes, maximumBytes);
                    }
                    finally
                    {
                        if (!accepted)
                        {
                            data?.Dispose();
                        }
                    }
                }
                throw new InvalidOperationException("H.264 rate-control quantization did not terminate.");
            }
            finally
            {
                if (!accepted)
                {
                    picture.Dispose();
                }
            }
        }
    }
}
