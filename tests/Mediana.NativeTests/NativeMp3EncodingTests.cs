// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeMp3EncodingTests
    {
        public static async Task RunAsync()
        {
            foreach ((int rate, int channels, int bitRate) in new[]
            {
                (48000, 2, 192000), (44100, 1, 128000), (32000, 2, 128000),
                (24000, 2, 96000), (22050, 1, 64000), (16000, 2, 64000),
                (12000, 1, 32000), (11025, 2, 64000), (8000, 1, 24000)
            }) { Mp3EncodeTestInput.Verify(rate, channels, bitRate); }
            Console.WriteLine("PASS MP3 encoding: all MPEG versions and sample rates, frame syntax, PCM quality and coded sample accounting.");
            await Mp3DecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
            await Console.Out.WriteLineAsync("PASS MP3 PCM: complete real files and all-rate stereo/window vectors, every sample, timing and retained ownership match.");
            Mp3HuffmanTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", "huffman-codes.tsv"));
            Console.WriteLine("PASS MP3 Huffman: all 1410 normative codewords, escape variants, signs, bit alignments and bounded quadruple endings match.");
            await Mp3SyntaxTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
            await Console.Out.WriteLineAsync("PASS MP3 spectrum: all 846 frames, protected-field CRCs, window parameters, scalefactors, complete bit-reservoir bytes and quantized coefficients match.");
            await Mp3ReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
            await Console.Out.WriteLineAsync("PASS MP3 framing: all independent MPEG1/2/2.5 packets, timestamps, ID3 text, picture bytes, trailing tags and ownership match.");
        }
    }
}
