// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    public class TgsKeyframeOrderTests
    {
        [Fact]
        public void RestoringOrderIsStableAndPreservesInput() => TgsKeyframeOrderTestInput.Verify();

        [Fact]
        public async Task RegisteredDecodingReportsRestoredOrderAndReleasesStorage()
        {
            MediaBufferAllocator allocator = new(16 * 8 * 4);
            List<string> warnings = [];
            TgsImageDecoder decoder = new(renderOptions: new() { Supersampling = 1 });
            using (VideoFrame frame = await decoder.DecodeAsync(TgsKeyframeOrderTestInput.Unordered,
                new ImageDecodeRequest { WarningHandler = warnings.Add }, allocator))
            {
                Assert.Equal(16 * 8 * 4, allocator.AllocatedBytes);
            }
            Assert.Single(warnings);
            Assert.Contains("reordered", warnings[0]);
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await decoder.DecodeAsync(
                TgsKeyframeOrderTestInput.Unordered, new() { WarningHandler = _ => throw new InvalidOperationException() }, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void MissingKeyframeTimeIsNotInvented()
        {
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(Encoding.UTF8.GetBytes("""
                {"v":"5.7","w":16,"h":8,"fr":30,"ip":0,"op":30,"layers":[
                  {"ty":1,"ks":{"o":{"a":1,"k":[{"t":0,"s":[100]},{"s":[0]}]}}}]}
                """)));
        }
    }
}
