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
            H264FrameRateBudget budget, int configurationBytes, CancellationToken token)
        {
            int maximumBytes = Math.Min(options.MaximumPacketBytes, budget.MaximumBytes);
            if (options.RateControl is not null)
            {
                maximumBytes = Math.Min(maximumBytes, LevelByteBudget(input, sequence, configurationBytes));
            }
            if (maximumBytes < 8)
            {
                throw new NotSupportedException("The H.264 rate budget cannot fit an encoded picture.");
            }
            int quantizer = budget.Quantizer;
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                H264Picture picture = new(sequence, allocator, keyFrame ? H264SliceKind.I : H264SliceKind.P);
                MediaBuffer? data = null;
                bool accepted = false;
                try
                {
                    int bytes;
                    data = keyFrame
                        ? H264IntraEncoder.Encode(input, picture, quantizer, options.Quantizer, idrId, allocator, maximumBytes,
                            options.RateControl is not null, out bytes, token)
                        : H264InterEncoder.Encode(input, picture, reference!, frameNumber, quantizer, allocator, options, maximumBytes,
                            options.RateControl is not null, out bytes, token);
                    if (data is not null)
                    {
                        H264EncodedPicture result = new(picture, data, quantizer);
                        accepted = true;
                        return result;
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
                        picture.Dispose();
                    }
                }
            }
            throw new InvalidOperationException("H.264 rate-control quantization did not terminate.");
        }

        private static int LevelByteBudget(VideoFrame input, H264SequenceParameterSet sequence, int configurationBytes)
        {
            Mediana.Core.Timing.MediaDuration duration = input.Duration!.Value;
            Int128 bitrateBytes = (Int128)H264EncoderConfiguration.MaximumBitsPerSecond * duration.Value * duration.TimeBase.Numerator /
                duration.TimeBase.Denominator / 8;
            long macroblocks = sequence.WidthInMbs * sequence.HeightInMapUnits;
            long pictureLimit = Math.Max(macroblocks * H264EncoderConfiguration.MaximumFramesPerSecond, H264EncoderConfiguration.MaximumMacroblocksPerSecond);
            long accessBytes = 384 * pictureLimit / (H264EncoderConfiguration.MaximumFramesPerSecond * H264EncoderConfiguration.MinimumCompressionRatio);
            return (int)Int128.Min(bitrateBytes, accessBytes - configurationBytes);
        }
    }
}
