// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Mp3;
using Mediana.Containers;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class AudioEncodeCommand
    {

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? streamIndex, AudioCodecMode codec, int bitRate, int? channels, ILogger logger, TextWriter writer, CancellationToken token)
        {
            (IAudioEncoderFactory Encoder, IContainerWriterFactory Muxer) format = codec switch
            {
                AudioCodecMode.Aac => (new AacEncoderFactory(new() { BitRate = bitRate }), new MpegTsWriterFactory()),
                AudioCodecMode.Mp3 => (new Mp3EncoderFactory(new() { BitRate = bitRate }), new Mp3WriterFactory()),
                _ => throw new ArgumentOutOfRangeException(nameof(codec))
            };
            MediaEngine engine = AudioEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie).AddAudioEncoder(format.Encoder)
                .AddContainerWriter(format.Muxer).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaBufferAllocator allocator = CommandMediaResources.CreateAllocator();
            MediaProgress progress = new(logger, TimeProvider.System);
            AudioTranscodeResult? result = null;
            await outputFile.WriteAsync(async destination =>
            {
                result = await engine.TranscodeAudioAsync(source, destination, format.Encoder.CodecId, format.Muxer.FormatId, allocator,
                    new() { StreamIndex = streamIndex, Channels = channels, Input = CommandMediaResources.OpenOptions }, progress.Report, token);
            }, token);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Audio encoding buffers were not released."); }
            await writer.WriteLineAsync($"{format.Encoder.CodecId} / {format.Muxer.FormatId}: {result!.SamplesPerChannel} input samples per channel; {result.Encoding.Packets} packets; priming {result.PrimingSamples}; padding {result.PaddingSamples}. Live buffer bytes: 0.");
            return 0;
        }
    }
}
