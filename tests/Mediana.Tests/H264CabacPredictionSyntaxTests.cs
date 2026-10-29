// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CabacPredictionSyntaxTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task PredictiveBinarizationsMatchIndependentExactIntervals(int initialization) =>
            await H264CabacPredictionTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"),
                Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), initialization);

        [Theory]
        [InlineData(32768)]
        [InlineData(-32769)]
        [InlineData(65536)]
        public async Task RejectsMotionDifferencesBeyondSupportedPrecision(int value)
        {
            H264SliceHeader header = await H264CabacPredictionTestInput.ReadHeaderAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-predictive-contexts.bin"));
            H264CabacIntervalWriter writer = H264CabacPredictionTestInput.CreateWriter(states, header);
            H264CabacPredictionTestInput.WriteMotion(writer, value, false, 0);
            writer.Termination(true);
            byte[] bytes = writer.ToEbsp();
            Assert.Throws<InvalidDataException>(() => ReadMotion(bytes, header));
        }

        [Fact]
        public async Task RejectsAReferenceOutsideTheActiveList()
        {
            H264SliceHeader header = await H264CabacPredictionTestInput.ReadHeaderAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-predictive-contexts.bin"));
            H264CabacIntervalWriter writer = H264CabacPredictionTestInput.CreateWriter(states, header);
            writer.Decision(true, 54, 0, 0);
            writer.Decision(true, 58, 0, 0);
            writer.Decision(false, 59, 0, 0);
            writer.Termination(true);
            byte[] bytes = writer.ToEbsp();
            Assert.Throws<InvalidDataException>(() => ReadReference(bytes, header));
        }

        private static int ReadMotion(byte[] bytes, H264SliceHeader header)
        {
            H264BitReader bits = new(bytes);
            H264CabacReader reader = new(ref bits, header);
            return H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, false, 0);
        }

        private static int ReadReference(byte[] bytes, H264SliceHeader header)
        {
            H264BitReader bits = new(bytes);
            H264CabacReader reader = new(ref bits, header);
            return H264CabacMacroblockSyntax.ReadReference(ref bits, reader, 0, 1);
        }
    }
}
