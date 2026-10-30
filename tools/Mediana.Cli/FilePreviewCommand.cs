// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class FilePreviewCommand
    {
        public static async Task<int> RunAsync(string input, string output, int? stream, long milliseconds, int page, CancellationToken token)
        {
            MediaEngine images = ImageEngineFactory.Create();
            byte[] probe = new byte[images.ImageProbeLength];
            await using FileStream file = File.OpenRead(input);
            int length = await file.ReadAtLeastAsync(probe, probe.Length, throwOnEndOfStream: false, cancellationToken: token);
            if (images.RecognizeImage(probe.AsSpan(0, length)) is not null)
            {
                if (stream is not null)
                {
                    throw new NotSupportedException("Image, document and animation previews do not select container streams.");
                }
                file.Position = 0;
                await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
                ImageDecodeRequest request = new()
                {
                    PageIndex = page - 1,
                    Time = new MediaTimestamp(milliseconds, new TimeBase(1, 1000))
                };
                return await ImagePreviewCommand.RunAsync(images, source, output, request, token);
            }
            if (page != 1)
            {
                throw new NotSupportedException("Video preview does not support document page selection.");
            }
            return await VideoPreviewCommand.RunAsync(input, output, stream, milliseconds, int.MaxValue, int.MaxValue, token);
        }
    }
}
