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
            foreach (string sample in new[] { "intrabcColor", "intrabcOdd", "intrabcLossless" })
            {
                Av1PictureSequenceTestInput.Verify(root, sample, [0x81, 0, 12, 0], [0, 1, 2]);
            }
            foreach (string sample in new[] { "intrabcSb128", "intrabcTiles" })
            {
                Av1PictureSequenceTestInput.Verify(root, sample, [0x81, 4, 12, 0], [0, 1, 2]);
            }
            Av1PictureSequenceTestInput.Verify(root, "intrabcMono", [0x81, 0, 28, 0], [0, 1, 2]);
            Av1PictureSequenceTestInput.Verify(root, "intrabcMixed", [0x81, 0, 12, 0]);
            Av1PictureSequenceTestInput.VerifyOwnership(root, "intrabcMixed", [0x81, 0, 12, 0]);
            Console.WriteLine("PASS AV1 intra block copy: 42 independent picture hashes, odd chroma motion, monochrome, tiles, large superblocks, lossless palettes, inter references and held output match.");
            Av1Main10TestInput.VerifyPictures(root);
            Av1Main10TestInput.VerifyOwnership(root);
            await Av1Main10ContainerTestInput.VerifyAsync(root, "mp4");
            await Av1Main10ContainerTestInput.VerifyAsync(root, "mkv");
            Console.WriteLine("PASS AV1 Main 10: 168 independent picture hashes, inter prediction, filters, monochrome, tiles, grain, super-resolution and MP4/Matroska seeks; held outputs, reset, cancellation and released leases match.");
            Av1SuperResolutionTestInput.VerifyPictures(root);
            Av1SuperResolutionTestInput.VerifyOwnership(root);
            Console.WriteLine("PASS AV1 super-resolution: 312 independent hashes across scales, tiles, restoration and grain; held outputs, reset and cancellation remain valid.");
            Av1GrainDisplayTestInput.VerifyPictures(root);
            Av1GrainDisplayTestInput.VerifyOwnership(root);
            Console.WriteLine("PASS AV1 film grain: 408 independent picture hashes, monochrome, clipped geometry, immutable prediction references, held output, reset and cancellation ownership match.");
        }
    }
}
