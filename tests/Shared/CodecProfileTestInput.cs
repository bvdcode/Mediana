// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    internal static class CodecProfileTestInput
    {
        public static void VerifyDeclarations()
        {
            (int Profile, int Constraints, int Level, string Name, string? LevelName)[] cases =
            [
                (44, 0x10, 31, "CAVLC 4:4:4 Intra", "3.1"),
                (66, 0, 31, "Baseline", "3.1"),
                (66, 0x40, 31, "Constrained Baseline", "3.1"),
                (77, 0x40, 31, "Main", "3.1"),
                (83, 0, 31, "Scalable Baseline", "3.1"),
                (86, 0, 31, "Scalable High", "3.1"),
                (88, 0, 31, "Extended", "3.1"),
                (100, 0, 31, "High", "3.1"),
                (100, 0x04, 31, "High", "3.1"),
                (100, 0x08, 31, "Progressive High", "3.1"),
                (100, 0x0c, 31, "Constrained High", "3.1"),
                (100, 0x10, 11, "High", "1.1"),
                (110, 0, 31, "High 10", "3.1"),
                (110, 0x08, 31, "Progressive High 10", "3.1"),
                (110, 0x10, 11, "High 10 Intra", "1.1"),
                (110, 0x18, 31, "High 10 Intra", "3.1"),
                (118, 0, 31, "Multiview High", "3.1"),
                (122, 0, 31, "High 4:2:2", "3.1"),
                (122, 0x10, 31, "High 4:2:2 Intra", "3.1"),
                (128, 0, 31, "Stereo High", "3.1"),
                (134, 0, 31, "MFC High", "3.1"),
                (135, 0, 31, "MFC Depth High", "3.1"),
                (138, 0, 31, "Multiview Depth High", "3.1"),
                (139, 0, 31, "Enhanced Multiview Depth High", "3.1"),
                (244, 0, 31, "High 4:4:4 Predictive", "3.1"),
                (244, 0x10, 31, "High 4:4:4 Intra", "3.1"),
                (66, 0, 11, "Baseline", "1.1"),
                (66, 0x10, 11, "Baseline", "1b"),
                (77, 0x10, 11, "Main", "1b"),
                (88, 0x10, 11, "Extended", "1b"),
                (100, 0, 9, "High", "1b"),
                (100, 0, 10, "High", "1"),
                (100, 0, 42, "High", "4.2"),
                (100, 0, 62, "High", "6.2"),
                (66, 0, 9, "Baseline", null),
                (100, 0, 0, "High", null),
                (100, 0, 255, "High", null)
            ];
            H264StreamMetadataReader reader = new();
            foreach ((int profile, int constraints, int level, string name, string? levelName) in cases)
            {
                byte[] sequence = H264MetadataTestInput.Sequence(profile: profile, constraints: constraints, level: level);
                byte[] configuration = H264MetadataTestInput.Configuration([sequence], [H264MetadataTestInput.Picture()]);
                VideoStreamMetadata metadata = (VideoStreamMetadata)reader.Read(H264MetadataTestInput.Stream(configuration))!;
                VideoStreamConfiguration value = metadata.Configurations.Single();
                if (value.CodecProfile?.Name != name || value.CodecProfile.Level != levelName
                    || value is not H264SequenceMetadata native || native.ProfileIdc != profile
                    || native.ConstraintFlags != constraints || native.LevelIdc != level)
                {
                    throw new InvalidDataException($"The AVC profile {profile}/{constraints}/{level} changed its declaration.");
                }
            }
        }

        public static void VerifyAlternatives()
        {
            byte[] first = H264MetadataTestInput.Sequence(id: 27, profile: 100, level: 41);
            byte[] second = H264MetadataTestInput.Sequence(id: 3, constraints: 0x50, level: 11);
            byte[] configuration = H264MetadataTestInput.Configuration([first, second], [H264MetadataTestInput.Picture(sequenceId: 27)]);
            VideoStreamMetadata metadata = (VideoStreamMetadata)new H264StreamMetadataReader()
                .Read(H264MetadataTestInput.Stream(configuration))!;
            if (metadata.Configurations.Count != 2 || metadata.Configurations[0].CodecProfile?.Name != "Constrained Baseline"
                || metadata.Configurations[0].CodecProfile?.Level != "1b" || metadata.Configurations[1].CodecProfile?.Name != "High"
                || metadata.Configurations[1].CodecProfile?.Level != "4.1")
            {
                throw new InvalidDataException("Opening AVC alternatives lost their distinct profile declarations.");
            }
        }
    }
}
