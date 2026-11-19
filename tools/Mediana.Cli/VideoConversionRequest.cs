// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal record VideoConversionRequest(H264EncoderOptions Options, int? Width, int? Height, bool VideoOnly) : ConversionRequest
    {
        public override Task<int> ExecuteAsync(string input, CommandOutputFile output, int? stream,
            ILogger logger, TextWriter writer, CancellationToken token) =>
            VideoTranscodeCommand.RunAsync(input, output, stream, Options, Width, Height, VideoOnly, logger, token);
    }
}
