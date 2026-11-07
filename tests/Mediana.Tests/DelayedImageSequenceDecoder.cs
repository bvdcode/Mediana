// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class DelayedImageSequenceDecoder : IImageDecoder, IImageSequenceDecoder
    {
        public string ImplementationId => "delayed.sequence";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Animation;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }
        public bool ThrowOnDecode { get; init; }
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("DSEQ"u8);

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDecode) { throw new InvalidDataException("Invalid recognized visual input."); }
            request.WarningHandler?.Invoke("Explicit image interpretation.");
            return ValueTask.FromResult(RgbaFrame.CopyFrom([21, 43, 65, 255], 1, 1, VideoColorInfo.Srgb, allocator, SampleAspectRatio.Square));
        }

        public async IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                yield return await DecodeAsync(encoded, request, allocator, cancellationToken);
                Entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally { Closed = true; }
        }
    }
}
