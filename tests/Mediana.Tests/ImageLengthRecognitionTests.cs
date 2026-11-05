// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ImageLengthRecognitionTests
    {
        [Fact]
        public async Task KnownAndUnknownInputLengthsSelectTheSameDecoder()
        {
            byte[] bytes = [.. "TEST"u8, 1, 2, 3, 4, 5, 6, 7, 8];
            LengthAwareImageDecoder decoder = new(bytes.Length);
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaBufferAllocator allocator = new(64 * 1024);
            using (VideoFrame frame = await engine.DecodeImageAsync(bytes, allocator)) { Assert.Equal(4, frame.Data.Length); }
            await using (ShortReadSource source = new(bytes))
            using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator)) { Assert.Equal(4, frame.Data.Length); }
            await using (StreamByteSource source = new(new MemoryStream(bytes)))
            using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator)) { Assert.Equal(4, frame.Data.Length); }
            Assert.Equal(3, decoder.DecodeCalls);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SequentialMetadataRecognitionFinishesAtEofWithoutDecoding()
        {
            byte[] bytes = [.. "TEST"u8, 1, 2, 3, 4];
            LengthAwareImageDecoder decoder = new(bytes.Length);
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            await using ShortReadSource source = new(bytes);
            MediaInfo info = await engine.ProbeAsync(source);
            Assert.Equal("length", info.Image!.FormatId);
            Assert.Equal(0, decoder.DecodeCalls);
        }

        [Fact]
        public void PrefixRecognitionRequiresTheCorrectCompleteLength()
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new LengthAwareImageDecoder(12)).Build();
            Assert.Null(engine.RecognizeImage("TEST"u8));
            Assert.Null(engine.RecognizeImage("TEST"u8, 13));
            Assert.Equal("length.image", engine.RecognizeImage("TEST"u8, 12));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.RecognizeImage("TEST"u8, 3));
        }
    }
}
