// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAv1Tests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Av1");
            await Av1ContainerTestInput.VerifyAsync(root, "mp4");
            await Av1ContainerTestInput.VerifyAsync(root, "mkv");
            await Av1ContainerTestInput.VerifyImplicitFinalSizesAsync(root);
            Console.WriteLine("PASS AV1 public containers: MP4, Matroska, implicit final OBU sizes, exact YUV, repeated seeking and released leases.");
            foreach (string sample in new[] { "timingEqual", "timingVariable", "timingModel" })
            {
                Av1TimingTestInput.Verify(root, sample);
            }
            Console.WriteLine("PASS AV1 timing: sequence clocks, decoder-model syntax, hidden and repeated pictures, exact samples and preserved container timestamps.");
            Av1PictureSequenceTestInput.Verify(root, "clippedCompound", [0x81, 0, 13, 0]);
            foreach (string sample in new[] { "tilesColumns", "tilesRows", "tilesGrid", "tilesGroups", "tilesNonuniform", "tilesSb128", "tilesTexture", "tilesUneven" })
            {
                Av1PictureSequenceTestInput.Verify(root, sample, [0x81, 0, 12, 0]);
            }
            Av1PictureSequenceTestInput.Verify(root, "tilesMono", [0x81, 0, 28, 0]);
            Console.WriteLine("PASS AV1 tiled pictures: context selection, separate tile groups, nonuniform and clipped geometry, monochrome and repeated reset match independent frame hashes.");
            Av1GrainDisplayTestInput.VerifyPictures(root);
            Av1GrainDisplayTestInput.VerifyOwnership(root);
            Console.WriteLine("PASS AV1 film grain: 408 independent picture hashes, monochrome, clipped geometry, immutable prediction references, held output, reset and cancellation ownership match.");
        }
    }
}
