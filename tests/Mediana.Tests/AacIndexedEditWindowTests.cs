// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AacIndexedEditWindowTests
    {
        [Theory]
        [InlineData("lc-mono-48")]
        [InlineData("lc-stereo-44")]
        public async Task IndexedMovieWindowsMatchIndependentPcmAndExactSampleInstants(string name)
        {
            await AacEditedAudioTestInput.VerifyTimeWindowAsync(Root, name, indexed: true);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 1)]
        [InlineData(false, 2)]
        [InlineData(false, 3)]
        [InlineData(false, 4)]
        [InlineData(false, 5)]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(true, 2)]
        [InlineData(true, 3)]
        [InlineData(true, 4)]
        [InlineData(true, 5)]
        public async Task MissingDelayedFractionalEmptyAndPastEofEditsPreserveExactWindows(bool wide, int mode)
        {
            (long Duration, long MediaTime, int Rate)[] edits = mode switch
            {
                0 => [],
                1 => [(123, -1, 65536), (117, 1537, 65536)],
                2 => [(1, 1537, 65536)],
                3 => [(2, -1, 65536), (0, 1537, 65536)],
                4 => [(117, 30001, 65536)],
                5 => [(13, -1, 65536), (900, 1024, 65536)],
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            foreach (string name in new[] { "lc-mono-48", "lc-stereo-44" })
            {
                byte[] encoded = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Path.Combine(Root, name + ".m4a")), wide, edits);
                await AacEditedAudioTestInput.VerifyTimeWindowAsync(Root, name, indexed: false, encoded);
                await AacEditedAudioTestInput.VerifyTimeWindowAsync(Root, name, indexed: true, encoded);
            }
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Aac");
    }
}
