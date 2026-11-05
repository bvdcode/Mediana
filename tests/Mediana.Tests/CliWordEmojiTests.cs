// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliWordEmojiTests
    {
        [Fact]
        public async Task WordPreviewUsesFluentWithoutAnInstalledEmojiFont()
        {
            string directory = Directory.CreateTempSubdirectory("mediana-word-emoji-").FullName;
            try
            {
                string input = Path.Combine(directory, "input.docx"), output = Path.Combine(directory, "output.png");
                await File.WriteAllBytesAsync(input, WordTestInput.Create("<w:p><w:r><w:t>😀👩‍💻❤️</w:t></w:r></w:p>", WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage));
                (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
                Assert.True(code == 0, error);
                Assert.DoesNotContain("missing-glyph", error, StringComparison.Ordinal);
                MediaBufferAllocator allocator = new(512_000);
                using VideoFrame frame = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(output), new(), allocator);
                Assert.Contains(frame.Data.ToArray().Chunk(4), pixel => pixel[0] != pixel[1] || pixel[1] != pixel[2]);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }
}
