// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    public class TgsStoredValuesTests
    {
        [Fact]
        public void ExplicitPoliciesPreserveStoredAnimationAndDiagnoseEachOmission() => TgsStoredValuesTestInput.Verify();

        [Fact]
        public void DefaultReaderStillRejectsMalformedPuppetKeysAndExpressions()
        {
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(TgsStoredValuesTestInput.Create(true, false)));
            Assert.Throws<NotSupportedException>(() => TgsReader.Read(TgsStoredValuesTestInput.Create(false, true)));
            Assert.Throws<NotSupportedException>(() => TgsReader.Read(TgsStoredValuesTestInput.Encoded, new() { IgnorePuppetEffects = true }));
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(TgsStoredValuesTestInput.Encoded, new() { IgnoreExpressions = true }));
        }

        [Fact]
        public void NamedPuppetRemainsUnsupportedWithoutExplicitOmission()
        {
            TgsDocument document = Read("\"ef\":[{\"ty\":34,\"mn\":\"ADBE FreePin3\",\"ef\":[{\"ty\":0,\"v\":{\"k\":1}}]}]", new());
            Assert.Single(document.UnsupportedFeatures);
            Assert.Throws<NotSupportedException>(() => TgsRenderer.RenderFrame(document, 0));
        }

        [Theory]
        [InlineData("\"ty\":34,\"mn\":\"Another effect\"")]
        [InlineData("\"ty\":34")]
        [InlineData("\"ty\":999,\"mn\":\"ADBE FreePin3\"")]
        public void OmissionDoesNotSwallowOtherEffects(string identity)
        {
            TgsDocument document = Read("\"ef\":[{" + identity + ",\"ef\":[]}]", TgsStoredValuesTestInput.Options);
            Assert.Single(document.UnsupportedFeatures);
            Assert.Empty(document.Warnings);
            Assert.Throws<NotSupportedException>(() => TgsRenderer.RenderFrame(document, 0));
        }

        [Theory]
        [InlineData("{\"x\":\"value\"}")]
        [InlineData("{\"x\":\"value\",\"k\":null}")]
        [InlineData("{\"x\":\"value\",\"k\":\"invalid\"}")]
        [InlineData("{\"x\":\"value\",\"a\":1,\"k\":[{\"t\":0,\"s\":[0]},{\"t\":null,\"s\":[1]}]}")]
        public void MissingValuesOrInvalidTimesOutsideOmittedEffectsRemainErrors(string property)
        {
            Assert.Throws<InvalidDataException>(() => Read("\"ks\":{\"o\":" + property + "}", TgsStoredValuesTestInput.Options));
        }

        [Fact]
        public void OmittedEffectsStillCountTowardsInputLimits()
        {
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(TgsStoredValuesTestInput.Encoded,
                TgsStoredValuesTestInput.Options with { MaximumItems = 30 }));
            Assert.Throws<InvalidDataException>(() => Read("""
                "ef":[{"ty":34,"mn":"ADBE FreePin3","ef":[{"ty":0,"v":{"k":1e999}}]}]
                """, TgsStoredValuesTestInput.Options));
        }

        [Fact]
        public async Task DecoderReportsOmissionsAndReleasesOutputWhenWarningHandlerThrows()
        {
            MediaBufferAllocator allocator = new(8 * 8 * 4);
            List<string> warnings = [];
            TgsImageDecoder decoder = new(TgsStoredValuesTestInput.Options, new() { Supersampling = 1 });
            using (VideoFrame frame = await decoder.DecodeAsync(TgsStoredValuesTestInput.Encoded,
                new() { WarningHandler = warnings.Add }, allocator))
            {
                Assert.Equal(8 * 8 * 4, allocator.AllocatedBytes);
            }
            Assert.Equal(2, warnings.Count);
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await decoder.DecodeAsync(
                TgsStoredValuesTestInput.Encoded, new() { WarningHandler = _ => throw new InvalidOperationException() }, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static TgsDocument Read(string properties, TgsReaderOptions options) => TgsReader.Read(Encoding.UTF8.GetBytes("""
            {"w":8,"h":8,"fr":30,"ip":0,"op":30,"layers":[{"ty":1,"sw":8,"sh":8,"sc":"#804020",
            """ + properties + "}]}"), options);
    }
}
