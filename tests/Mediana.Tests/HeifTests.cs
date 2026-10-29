// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Tests
{
    public class HeifTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Heif");

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
