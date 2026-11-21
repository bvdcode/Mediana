// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Imaging;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class IccCmykTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "IccCmyk");

        [Fact]
        public Task LegacyAndMultiStageTransformsMatchLittleCms() => IccCmykTestInput.VerifyAsync(Root);

        [Fact]
        public Task JpegInkSamplesAndSrgbMatchIndependentDecoders() =>
            JpegCmykIccTestInput.VerifyAsync(Root, Path.Combine(AppContext.BaseDirectory, "Data", "Raster"));

        [Fact]
        public async Task EmbeddedImagePipelineAppliesIccAndExifOrientationWithoutMissingColorWarnings()
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Raster", "ycck-multiscan.jpg"));
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "v2-mft2-lab.icc"));
            byte[] encoded = JpegCmykIccTestInput.Attach(original, profile);
            byte[] rotated = JpegTestData.Insert(encoded, 225, [.. "Exif\0\0"u8, .. ImageExifOrientationTestInput.Metadata(6, true)]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            ImageDecodeContext context = new([new JpegImageDecoder()]);
            ImageDecodeRequest request = new() { WarningHandler = _ => throw new InvalidOperationException("A complete ICC profile needs no missing-color warning.") };
            using (VideoFrame control = await ImageResources.DecodeAsync(encoded, context, request, allocator))
            using (VideoFrame actual = await ImageResources.DecodeAsync(rotated, context, request, allocator))
            {
                Assert.Equal(control.Format.Width, actual.Format.Height);
                Assert.Equal(control.Format.Height, actual.Format.Width);
                for (int y = 0; y < control.Format.Height; y++)
                {
                    for (int x = 0; x < control.Format.Width; x++)
                    {
                        Assert.True(control.GetRow(0, y).Slice(x * 4, 4).SequenceEqual(
                            actual.GetRow(0, x).Slice((control.Format.Height - 1 - y) * 4, 4)));
                    }
                }
                Assert.Equal(VideoColorInfo.Srgb, actual.Format.Color);
                Assert.Null(actual.Format.ColorDescription);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SharedTransformSupportsConcurrencyCancellationAndExactFrameBudget()
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "v4-mab2-lab.icc"));
            byte[] samples = await File.ReadAllBytesAsync(Path.Combine(Root, "samples.cmyk"));
            MediaBufferAllocator allocator = new(samples.Length);
            using (VideoFrame frame = IccCmykTestInput.CreateFrame(profile, samples, 64, 64, allocator))
            {
                profile.AsSpan().Clear();
                MediaBufferAllocator outputs = new(samples.Length * 4);
                Task<byte[]>[] tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    using VideoFrame output = SrgbConverter.Apply(frame, outputs);
                    return output.Data.ToArray();
                })).ToArray();
                byte[][] results = await Task.WhenAll(tasks);
                foreach (byte[] output in results) { Assert.Equal(results[0], output); }
                Assert.Equal(0, outputs.AllocatedBytes);
                Assert.ThrowsAny<OperationCanceledException>(() => SrgbConverter.Apply(frame, outputs, new(true)));
                MediaBufferAllocator small = new(samples.Length - 1);
                Assert.Throws<MediaMemoryLimitException>(() => SrgbConverter.Apply(frame, small));
                Assert.Equal(0, small.AllocatedBytes);
                MediaBufferAllocator fitted = new(13 * 13 * 4);
                using (VideoFrame preview = SrgbPreview.Fit(frame, VideoDisplayMatrix.Identity, 13, 13, fitted))
                { Assert.Equal(13 * 13 * 4, fitted.AllocatedBytes); }
                Assert.Equal(0, fitted.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("channels")]
        [InlineData("grid")]
        [InlineData("input-count")]
        [InlineData("legacy-matrix")]
        [InlineData("mab-offset")]
        [InlineData("mab-overlap")]
        [InlineData("mab-missing-a")]
        [InlineData("mab-missing-m")]
        [InlineData("mab-grid")]
        [InlineData("mab-precision")]
        [InlineData("mab-reserved")]
        [InlineData("mab-curve")]
        [InlineData("mab-padding")]
        public async Task MalformedSelectedTransformFailsWithoutRetryingDefaultOrRetainingOutput(string mutation)
        {
            string name = mutation.StartsWith("mab-", StringComparison.Ordinal) ? "v4-mab2-lab" : "v2-mft2-lab";
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, name + ".icc"));
            int start = Tag(profile, "A2B1"u8);
            int clut = name.StartsWith("v4", StringComparison.Ordinal) ? (int)Read(profile, start + 24) : 0;
            switch (mutation)
            {
                case "channels": profile[start + 8] = 3; break;
                case "grid": profile[start + 10] = 255; break;
                case "input-count": profile.AsSpan(start + 48, 2).Clear(); break;
                case "legacy-matrix": profile.AsSpan(start + 12, 4).Clear(); break;
                case "mab-offset": Write(profile, start + 28, uint.MaxValue); break;
                case "mab-overlap": Write(profile, start + 16, (uint)clut); break;
                case "mab-missing-a": Write(profile, start + 28, 0); break;
                case "mab-missing-m": Write(profile, start + 20, 0); break;
                case "mab-grid": profile.AsSpan(start + clut, 4).Fill(255); break;
                case "mab-precision": profile[start + clut + 16] = 4; break;
                case "mab-reserved": profile[start + clut + 17] = 1; break;
                case "mab-curve": Write(profile, start + (int)Read(profile, start + 12) + 8, uint.MaxValue); break;
                case "mab-padding": profile[start + (int)Read(profile, start + 12) + 14] = 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            MediaBufferAllocator allocator = new(64);
            using (VideoFrame frame = IccCmykTestInput.CreateFrame(profile, [0, 128, 64, 200], 1, 1, allocator))
            {
                Assert.Throws<InvalidDataException>(() => SrgbConverter.Apply(frame, allocator));
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("color-space")]
        [InlineData("pcs")]
        [InlineData("selected-type")]
        public async Task UnsupportedModelsFailExplicitly(string mutation)
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "v2-mft2-lab.icc"));
            switch (mutation)
            {
                case "color-space": "RGB "u8.CopyTo(profile.AsSpan(16)); break;
                case "pcs": "Luv "u8.CopyTo(profile.AsSpan(20)); break;
                case "selected-type": "mpet"u8.CopyTo(profile.AsSpan(Tag(profile, "A2B1"u8))); break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            MediaBufferAllocator allocator = new(64);
            using VideoFrame frame = IccCmykTestInput.CreateFrame(profile, [0, 128, 64, 200], 1, 1, allocator);
            Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(frame, allocator));
            Assert.Equal(4, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RelativeColorimetricTableTakesPrecedenceOverPerceptual()
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "v2-mft2-lab.icc"));
            MediaBufferAllocator allocator = new(64);
            using VideoFrame original = IccCmykTestInput.CreateFrame(profile, [0, 128, 64, 200], 1, 1, allocator);
            "mpet"u8.CopyTo(profile.AsSpan(Tag(profile, "A2B0"u8)));
            using VideoFrame modified = IccCmykTestInput.CreateFrame(profile, original.Data.Span, 1, 1, allocator);
            using VideoFrame first = SrgbConverter.Apply(original, allocator);
            using VideoFrame second = SrgbConverter.Apply(modified, allocator);
            Assert.Equal(first.Data.ToArray(), second.Data.ToArray());
        }

        [Fact]
        public async Task MatrixValuesAreClippedBeforeTheOutputCurves()
        {
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(Root, "v4-mab2-xyz.icc"));
            int start = Tag(profile, "A2B1"u8), matrix = start + (int)Read(profile, start + 16);
            profile.AsSpan(matrix, 48).Clear();
            Write(profile, matrix + 36, 6554); Write(profile, matrix + 44, 6554);
            MediaBufferAllocator allocator = new(64);
            using VideoFrame clipped = IccCmykTestInput.CreateFrame(profile, [128, 128, 128, 128], 1, 1, allocator);
            Write(profile, matrix + 40, unchecked((uint)-6554));
            using VideoFrame negative = IccCmykTestInput.CreateFrame(profile, clipped.Data.Span, 1, 1, allocator);
            using VideoFrame first = SrgbConverter.Apply(clipped, allocator);
            using VideoFrame second = SrgbConverter.Apply(negative, allocator);
            Assert.Equal(first.Data.ToArray(), second.Data.ToArray());
        }

        private static int Tag(ReadOnlySpan<byte> profile, ReadOnlySpan<byte> signature)
        {
            int count = (int)Read(profile, 128);
            for (int index = 0; index < count; index++)
            {
                int offset = 132 + index * 12;
                if (profile.Slice(offset, 4).SequenceEqual(signature)) { return (int)Read(profile, offset + 4); }
            }
            throw new InvalidDataException("Required test profile tag is missing.");
        }

        private static uint Read(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
        private static void Write(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes[offset..], value);
    }
}
