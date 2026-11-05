// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif;
using Mediana.Images.Heif.Container;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using System.Runtime.InteropServices;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Tests
{
    public class HeifTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Heif");

        [Fact]
        public async Task MetadataRetainsNativeDeclarationsAndOwnedExifWithoutDecodingSamples() =>
            await HeifExifTestInput.VerifyAsync(Root);

        [Fact]
        public async Task ParallelWavefrontSamplesAndCancellationAreCorrect()
        {
            await HeifReferenceTestInput.VerifyWavefrontAsync();
            IntraSliceHeader slice = await IntraPictureFixture.ReadSliceAsync("wpp-8bit.hevc");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await new IntraPictureDecoder(slice).DecodeAsync(4, new CancellationToken(true)).WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task AllFortyTwoIndependentRgbaReferencesMatch()
        {
            await HeifReferenceTestInput.VerifyAllAsync(Root);
        }

        [Fact]
        public async Task RegisteredFramesRemainIndependentAfterInputAndWorkspaceReuse() =>
            await HeifFrameOwnershipTestInput.VerifyAsync(Root);

        [Fact]
        public async Task OutputBudgetIsReservedBeforePictureDecodingAndReleasedOnFailure()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "basic-8bit.heic"));
            HeifContainer container = new HeifContainerReader(HeicDecodeOptions.Default).Read(data);
            ReadOnlyMemory<byte> coded = container.ReadData(container.PrimaryItemId);
            Assert.True(MemoryMarshal.TryGetArray(coded, out ArraySegment<byte> segment));
            Assert.Same(data, segment.Array);
            data.AsSpan(segment.Offset, segment.Count).Clear();
            HeicImageDecoder decoder = new(SampleAspectRatio.Square);
            MediaBufferAllocator insufficient = new(4);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await decoder.DecodeAsync(data, new(), insufficient));
            MediaBufferAllocator output = new(64 * 48 * 4);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.DecodeAsync(data, new(), output));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await decoder.DecodeAsync(data, new(), output, new(true)));
            Assert.Equal(0, insufficient.AllocatedBytes);
            Assert.Equal(0, output.AllocatedBytes);
            byte[] profiled = await File.ReadAllBytesAsync(Path.Combine(Root, "basic-8bit.heic"));
            int nclx = profiled.AsSpan().IndexOf("nclx"u8);
            Assert.True(nclx >= 0);
            "prof"u8.CopyTo(profiled.AsSpan(nclx, 4));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await decoder.DecodeAsync(profiled, new(), output));
            Assert.Equal(0, output.AllocatedBytes);
        }

        [Fact]
        public async Task OwnsInputAndIndependentConcurrentOutput()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "grid-alpha.heic"));
            HeicDocument document = HeicDocument.Load(data);
            data.AsSpan().Clear();
            RgbaImage[] images = await Task.WhenAll(document.DecodePrimaryAsync(), document.DecodePrimaryAsync());
            Assert.Equal(images[0].Pixels.ToArray(), images[1].Pixels.ToArray());
            Assert.False(images[0].Pixels.Equals(images[1].Pixels));
            RgbaImage selected = await document.DecodeImageAsync(document.PrimaryItemId);
            Assert.Equal(images[0].Pixels.ToArray(), selected.Pixels.ToArray());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.DecodeImageAsync(uint.MaxValue));
        }

        [Fact]
        public async Task InputPixelItemLimitsAndMalformedInputAreRejected()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "grid.heic"));
            Assert.Throws<InvalidDataException>(() => HeicDocument.Load(data, new(maxInputBytes: data.Length - 1)));
            Assert.Throws<InvalidDataException>(() => HeicDocument.Load(data, new(maxPixels: 1)));
            Assert.Throws<InvalidDataException>(() => HeicDocument.Load(data, new(maxItems: 1)));
            foreach (int length in new[] { 0, 1, 8, 16, data.Length / 2, data.Length - 1 })
            {
                Assert.Throws<InvalidDataException>(() => HeicDocument.Load(data.AsMemory(0, length)));
            }
        }

        [Fact]
        public async Task LoadingPreservesStreamOwnershipAndObservesCancellation()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "basic-8bit.heic"));
            using MemoryStream stream = new(data);
            HeicDocument document = await HeicDocument.LoadAsync(stream);
            Assert.True(stream.CanRead);
            RgbaImage image = await document.DecodePrimaryAsync();
            Assert.Equal(64, image.Width);
            Assert.Equal(48, image.Height);
            stream.Position = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HeicDocument.LoadAsync(stream, cancellationToken: new CancellationToken(true)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.DecodePrimaryAsync(new CancellationToken(true)));
        }
    }
}
